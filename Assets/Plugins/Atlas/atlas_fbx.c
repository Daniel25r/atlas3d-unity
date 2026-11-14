// ATLAS 3D - FBX / OBJ reader for the Android app, on top of ufbx (MIT,
// https://github.com/ufbx/ufbx, the same reader Godot uses). It loads the
// file and writes everything ATLAS needs into one simple binary block:
// nodes, meshes (triangles, normals, UVs, skin weights), materials,
// textures (embedded data or file name), skins and the animations sampled
// at 30 fps. The C# side turns that block into a GLB.
#include "ufbx.h"
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

typedef struct { uint8_t *data; size_t size, cap; int failed; } abuf;

static void ab_reserve(abuf *b, size_t extra) {
	if (b->failed) return;
	if (b->size + extra <= b->cap) return;
	size_t cap = b->cap ? b->cap * 2 : 1 << 16;
	while (cap < b->size + extra) cap *= 2;
	uint8_t *d = (uint8_t*)realloc(b->data, cap);
	if (!d) { b->failed = 1; return; }
	b->data = d; b->cap = cap;
}
static void ab_bytes(abuf *b, const void *p, size_t n) {
	ab_reserve(b, n);
	if (b->failed) return;
	if (n) memcpy(b->data + b->size, p, n);
	b->size += n;
}
static void ab_i32(abuf *b, int32_t v) { ab_bytes(b, &v, 4); }
static void ab_f32(abuf *b, double v) { float f = (float)v; ab_bytes(b, &f, 4); }
static void ab_str(abuf *b, const char *s, size_t n) { ab_i32(b, (int32_t)n); ab_bytes(b, s, n); }

typedef struct { float p[3], n[3], uv[2]; uint16_t j[4]; float w[4]; } avert;

static char g_error[512];

const char *atlas_fbx_error(void) { return g_error; }

void atlas_fbx_free(uint8_t *p) { free(p); }

static void write_matrix(abuf *b, const ufbx_matrix *m) {
	// glTF: 4x4, column-major
	ab_f32(b, m->m00); ab_f32(b, m->m10); ab_f32(b, m->m20); ab_f32(b, 0);
	ab_f32(b, m->m01); ab_f32(b, m->m11); ab_f32(b, m->m21); ab_f32(b, 0);
	ab_f32(b, m->m02); ab_f32(b, m->m12); ab_f32(b, m->m22); ab_f32(b, 0);
	ab_f32(b, m->m03); ab_f32(b, m->m13); ab_f32(b, m->m23); ab_f32(b, 1);
}

static void write_transform(abuf *b, ufbx_transform t) {
	ab_f32(b, t.translation.x); ab_f32(b, t.translation.y); ab_f32(b, t.translation.z);
	ab_f32(b, t.rotation.x); ab_f32(b, t.rotation.y); ab_f32(b, t.rotation.z); ab_f32(b, t.rotation.w);
	ab_f32(b, t.scale.x); ab_f32(b, t.scale.y); ab_f32(b, t.scale.z);
}

static ufbx_texture *file_texture(ufbx_texture *t) {
	if (!t) return NULL;
	if (t->type == UFBX_TEXTURE_FILE) return t;
	if (t->file_textures.count > 0) return t->file_textures.data[0];
	return NULL;
}

static void write_mesh(abuf *b, ufbx_mesh *mesh) {
	ufbx_skin_deformer *skin = mesh->skin_deformers.count > 0 ? mesh->skin_deformers.data[0] : NULL;
	size_t max_tri = mesh->max_face_triangles > 0 ? mesh->max_face_triangles : 1;
	uint32_t *tri = (uint32_t*)malloc(sizeof(uint32_t) * max_tri * 3);
	size_t total = mesh->num_triangles * 3;
	avert *verts = (avert*)calloc(total > 0 ? total : 1, sizeof(avert));
	uint32_t *part_of = (uint32_t*)calloc(total > 0 ? total : 1, sizeof(uint32_t));
	size_t nv = 0;
	size_t nparts = mesh->material_parts.count > 0 ? mesh->material_parts.count : 1;
	size_t *part_counts = (size_t*)calloc(nparts, sizeof(size_t));
	for (size_t pi = 0; pi < nparts && tri && verts; pi++) {
		size_t nfaces = mesh->material_parts.count > 0 ? mesh->material_parts.data[pi].face_indices.count : mesh->num_faces;
		for (size_t fi = 0; fi < nfaces; fi++) {
			uint32_t face_index = mesh->material_parts.count > 0 ? mesh->material_parts.data[pi].face_indices.data[fi] : (uint32_t)fi;
			ufbx_face face = mesh->faces.data[face_index];
			uint32_t nt = ufbx_triangulate_face(tri, max_tri * 3, mesh, face);
			for (uint32_t k = 0; k < nt * 3 && nv < total; k++) {
				uint32_t ix = tri[k];
				avert *v = &verts[nv];
				ufbx_vec3 p = ufbx_get_vertex_vec3(&mesh->vertex_position, ix);
				v->p[0] = (float)p.x; v->p[1] = (float)p.y; v->p[2] = (float)p.z;
				if (mesh->vertex_normal.exists) {
					ufbx_vec3 n = ufbx_get_vertex_vec3(&mesh->vertex_normal, ix);
					v->n[0] = (float)n.x; v->n[1] = (float)n.y; v->n[2] = (float)n.z;
				} else v->n[1] = 1.0f;
				if (mesh->vertex_uv.exists) {
					ufbx_vec2 uv = ufbx_get_vertex_vec2(&mesh->vertex_uv, ix);
					v->uv[0] = (float)uv.x; v->uv[1] = (float)(1.0 - uv.y);   // glTF: origin at the top
				}
				if (skin) {
					uint32_t vi = mesh->vertex_indices.data[ix];
					if (vi < skin->vertices.count) {
						ufbx_skin_vertex sv = skin->vertices.data[vi];
						float sum = 0.0f;
						for (uint32_t w = 0; w < sv.num_weights && w < 4; w++) {
							ufbx_skin_weight sw = skin->weights.data[sv.weight_begin + w];
							v->j[w] = (uint16_t)sw.cluster_index;
							v->w[w] = (float)sw.weight;
							sum += v->w[w];
						}
						if (sum > 0.0f) for (int w = 0; w < 4; w++) v->w[w] /= sum;
						else v->w[0] = 1.0f;
					} else v->w[0] = 1.0f;
				}
				part_of[nv] = (uint32_t)pi;
				part_counts[pi]++;
				nv++;
			}
		}
	}
	// Shared vertices (identical corners become one).
	uint32_t *indices = (uint32_t*)malloc(sizeof(uint32_t) * (nv > 0 ? nv : 1));
	size_t unique = 0;
	if (nv > 0 && indices) {
		ufbx_vertex_stream stream = { verts, nv, sizeof(avert) };
		ufbx_error err;
		unique = ufbx_generate_indices(&stream, 1, indices, nv, NULL, &err);
		if (err.type != UFBX_ERROR_NONE) {
			unique = nv;
			for (size_t i = 0; i < nv; i++) indices[i] = (uint32_t)i;
		}
	}
	ab_str(b, mesh->name.data, mesh->name.length);
	ab_i32(b, (int32_t)unique);
	ab_i32(b, mesh->vertex_uv.exists ? 1 : 0);
	ab_i32(b, skin ? 1 : 0);
	for (size_t i = 0; i < unique; i++) ab_bytes(b, verts[i].p, 12);
	for (size_t i = 0; i < unique; i++) ab_bytes(b, verts[i].n, 12);
	if (mesh->vertex_uv.exists) for (size_t i = 0; i < unique; i++) ab_bytes(b, verts[i].uv, 8);
	if (skin) {
		for (size_t i = 0; i < unique; i++) ab_bytes(b, verts[i].j, 8);
		for (size_t i = 0; i < unique; i++) ab_bytes(b, verts[i].w, 16);
	}
	// Parts: material + triangles (the corners kept their part order).
	int32_t used_parts = 0;
	for (size_t pi = 0; pi < nparts; pi++) if (part_counts[pi] > 0) used_parts++;
	ab_i32(b, used_parts);
	size_t at = 0;
	for (size_t pi = 0; pi < nparts; pi++) {
		size_t c = part_counts[pi];
		if (c == 0) continue;
		int32_t mat = -1;
		if (mesh->materials.count > 0) {
			uint32_t mi = mesh->material_parts.count > 0 ? mesh->material_parts.data[pi].index : 0;
			if (mi < mesh->materials.count && mesh->materials.data[mi]) mat = (int32_t)mesh->materials.data[mi]->typed_id;
		}
		ab_i32(b, mat);
		ab_i32(b, (int32_t)c);
		ab_bytes(b, indices + at, c * 4);
		at += c;
	}
	// Skin: cluster -> bone node + inverse bind matrix.
	if (skin) {
		ab_i32(b, (int32_t)skin->clusters.count);
		for (size_t i = 0; i < skin->clusters.count; i++) {
			ufbx_skin_cluster *cl = skin->clusters.data[i];
			ab_i32(b, cl->bone_node ? (int32_t)cl->bone_node->typed_id : -1);
			write_matrix(b, &cl->geometry_to_bone);
		}
	}
	free(tri); free(verts); free(part_of); free(part_counts); free(indices);
}

static double prop_real(const ufbx_material_map *m, double def) { return m->has_value ? m->value_real : def; }

/// Loads `path` and returns the block (free it with atlas_fbx_free), or NULL
/// with the reason in atlas_fbx_error().
uint8_t *atlas_fbx_load(const char *path, int32_t *out_size) {
	g_error[0] = 0;
	*out_size = 0;
	ufbx_load_opts opts;
	memset(&opts, 0, sizeof(opts));
	opts.target_axes = ufbx_axes_right_handed_y_up;
	opts.target_unit_meters = 1.0f;
	opts.space_conversion = UFBX_SPACE_CONVERSION_TRANSFORM_ROOT;
	opts.geometry_transform_handling = UFBX_GEOMETRY_TRANSFORM_HANDLING_HELPER_NODES;
	opts.generate_missing_normals = true;
	opts.load_external_files = true;
	opts.ignore_missing_external_files = true;
	ufbx_error error;
	ufbx_scene *scene = ufbx_load_file(path, &opts, &error);
	if (!scene) {
		ufbx_format_error(g_error, sizeof(g_error), &error);
		return NULL;
	}
	abuf b; memset(&b, 0, sizeof(b));
	ab_bytes(&b, "AFBX", 4);
	ab_i32(&b, 1);
	// Nodes (the root holds the axis / unit conversion)
	ab_i32(&b, (int32_t)scene->nodes.count);
	for (size_t i = 0; i < scene->nodes.count; i++) {
		ufbx_node *n = scene->nodes.data[i];
		ab_str(&b, n->name.data, n->name.length);
		ab_i32(&b, n->parent ? (int32_t)n->parent->typed_id : -1);
		write_transform(&b, n->local_transform);
		ab_i32(&b, n->mesh ? (int32_t)n->mesh->typed_id : -1);
		ab_i32(&b, n->bone ? 1 : 0);
	}
	// Meshes
	ab_i32(&b, (int32_t)scene->meshes.count);
	for (size_t i = 0; i < scene->meshes.count; i++) write_mesh(&b, scene->meshes.data[i]);
	// Textures (only file textures; layered ones point at their first file)
	ab_i32(&b, (int32_t)scene->textures.count);
	for (size_t i = 0; i < scene->textures.count; i++) {
		ufbx_texture *t = file_texture(scene->textures.data[i]);
		if (!t) { ab_str(&b, "", 0); ab_str(&b, "", 0); ab_i32(&b, 0); continue; }
		ab_str(&b, t->filename.data, t->filename.length);
		ab_str(&b, t->relative_filename.data, t->relative_filename.length);
		ab_i32(&b, (int32_t)t->content.size);
		ab_bytes(&b, t->content.data, t->content.size);
	}
	// Materials
	ab_i32(&b, (int32_t)scene->materials.count);
	for (size_t i = 0; i < scene->materials.count; i++) {
		ufbx_material *m = scene->materials.data[i];
		ab_str(&b, m->name.data, m->name.length);
		ufbx_vec4 c = m->pbr.base_color.has_value ? m->pbr.base_color.value_vec4 : m->fbx.diffuse_color.value_vec4;
		double f = prop_real(&m->pbr.base_factor, 1.0);
		if (!m->pbr.base_color.has_value && m->fbx.diffuse_factor.has_value) f = m->fbx.diffuse_factor.value_real;
		double opacity = m->pbr.opacity.has_value ? m->pbr.opacity.value_real : 1.0;
		if (!m->pbr.opacity.has_value && m->fbx.transparency_factor.has_value) opacity = 1.0 - m->fbx.transparency_factor.value_real;
		if (opacity <= 0.001) opacity = 1.0;   // many exporters write 0 by mistake
		ab_f32(&b, c.x * f); ab_f32(&b, c.y * f); ab_f32(&b, c.z * f); ab_f32(&b, opacity);
		ab_f32(&b, prop_real(&m->pbr.metalness, 0.0));
		ab_f32(&b, prop_real(&m->pbr.roughness, 0.6));
		ufbx_vec3 e = m->pbr.emission_color.value_vec3;
		double ef = prop_real(&m->pbr.emission_factor, 0.0);
		ab_f32(&b, e.x * ef); ab_f32(&b, e.y * ef); ab_f32(&b, e.z * ef);
		ufbx_texture *tex = m->pbr.base_color.texture ? m->pbr.base_color.texture : m->fbx.diffuse_color.texture;
		ab_i32(&b, tex ? (int32_t)tex->typed_id : -1);
		ufbx_texture *nt = m->pbr.normal_map.texture ? m->pbr.normal_map.texture : m->fbx.normal_map.texture;
		ab_i32(&b, nt ? (int32_t)nt->typed_id : -1);
	}
	// Animations: every take, sampled at 30 fps (local transforms of all nodes).
	int32_t nstacks = 0;
	for (size_t s = 0; s < scene->anim_stacks.count; s++) {
		ufbx_anim_stack *st = scene->anim_stacks.data[s];
		if (st->time_end > st->time_begin) nstacks++;
	}
	ab_i32(&b, nstacks);
	for (size_t s = 0; s < scene->anim_stacks.count; s++) {
		ufbx_anim_stack *st = scene->anim_stacks.data[s];
		if (!(st->time_end > st->time_begin)) continue;
		double len = st->time_end - st->time_begin;
		int32_t frames = (int32_t)(len * 30.0 + 0.5) + 1;
		if (frames > 3600) frames = 3600;
		if (frames < 2) frames = 2;
		ab_str(&b, st->name.data, st->name.length);
		ab_i32(&b, frames);
		ab_f32(&b, 30.0);
		for (int32_t f = 0; f < frames; f++) {
			double t = st->time_begin + (double)f / 30.0;
			if (t > st->time_end) t = st->time_end;
			for (size_t i = 0; i < scene->nodes.count; i++)
				write_transform(&b, ufbx_evaluate_transform(st->anim, scene->nodes.data[i], t));
		}
	}
	ufbx_free_scene(scene);
	if (b.failed) {
		free(b.data);
		strcpy(g_error, "sin memoria");
		return NULL;
	}
	*out_size = (int32_t)b.size;
	return b.data;
}
