// ATLAS 3D (Unity) - the inspector (properties panel), with the sections
// of the Godot version: the object's header (name, visible, locked),
// Transformación, Material (presets, colour, metal, roughness, glow,
// opacity, texture), Luz (colour, temperature, intensity, range, angle,
// shadows), the model's own animations, keys and info. With nothing
// selected: «Ambiente rápido», image effects, view and the object list.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        const float PropsWidth = 380f;
        const float InnerWidth = PropsWidth - 24f - 8f;   // panel padding, section padding

        GameObject propsPanel;
        Text propsTitle;
        RectTransform inspector;
        ScrollRect inspectorScroll;
        bool inspectorDirty;
        InputField nameField;
        readonly InputField[] tf = new InputField[9];
        readonly Dictionary<string, bool> sectionOpen = new Dictionary<string, bool>();

        static readonly Color[] Swatches =
        {
            AtlasTheme.Hex("ffffff"), AtlasTheme.Hex("e8eaf0"), AtlasTheme.Hex("9aa0ab"), AtlasTheme.Hex("2a2d36"),
            AtlasTheme.Hex("e5484d"), AtlasTheme.Hex("f5a524"), AtlasTheme.Hex("f2d74e"), AtlasTheme.Hex("46c46e"),
            AtlasTheme.Hex("2fb5a8"), AtlasTheme.Hex("3d8bfd"), AtlasTheme.Hex("6c5ce7"), AtlasTheme.Hex("b25cff"),
            AtlasTheme.Hex("ff6fb5"), AtlasTheme.Hex("8a6a4f"), AtlasTheme.Hex("e0b089"), AtlasTheme.Hex("111216"),
        };

        class MatPreset
        {
            public string name;
            public bool keepColor;
            public Color color;
            public float metallic, roughness, emission = 0f, opacity = 1f;
        }

        static readonly MatPreset[] MatPresets =
        {
            new MatPreset { name = "Pintura de coche", keepColor = true, metallic = 0.55f, roughness = 0.18f },
            new MatPreset { name = "Cromo", color = new Color(0.96f, 0.96f, 0.98f), metallic = 1f, roughness = 0.04f },
            new MatPreset { name = "Oro", color = new Color(1f, 0.77f, 0.33f), metallic = 1f, roughness = 0.18f },
            new MatPreset { name = "Metal cepillado", color = new Color(0.72f, 0.73f, 0.75f), metallic = 1f, roughness = 0.42f },
            new MatPreset { name = "Vidrio", color = new Color(0.82f, 0.9f, 1f), metallic = 0f, roughness = 0.02f, opacity = 0.25f },
            new MatPreset { name = "Goma", color = new Color(0.035f, 0.035f, 0.04f), metallic = 0f, roughness = 0.92f },
            new MatPreset { name = "Plástico", keepColor = true, metallic = 0f, roughness = 0.38f },
            new MatPreset { name = "Arcilla", color = new Color(0.78f, 0.6f, 0.5f), metallic = 0f, roughness = 0.85f },
            new MatPreset { name = "Neón", keepColor = true, metallic = 0f, roughness = 0.5f, emission = 4f },
            new MatPreset { name = "Mate", keepColor = true, metallic = 0f, roughness = 1f },
        };

        void BuildProps()
        {
            var panel = AtlasTheme.Box(viewArea, "Propiedades", AtlasTheme.Panel, 18);
            var r = panel.rectTransform;
            r.anchorMin = new Vector2(1f, 0f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.offsetMin = new Vector2(-(PropsWidth + 10f), 10f);
            r.offsetMax = new Vector2(-10f, -82f);
            propsPanel = panel.gameObject;

            var head = AtlasTheme.Rect(panel.transform, "Titulo");
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(0f, 52f);
            head.anchoredPosition = Vector2.zero;
            AtlasTheme.Row(head.gameObject, 6f, new RectOffset(18, 8, 6, 0));
            propsTitle = AtlasTheme.Label(head, "PROPIEDADES", 15, AtlasTheme.TextDim, true);
            AtlasTheme.Le(propsTitle.gameObject, 44f, -1f, 1f);
            AtlasTheme.IconButton(head, "close", ToggleProps, 42f, 20f);

            var body = AtlasTheme.Rect(panel.transform, "Cuerpo");
            AtlasTheme.Fill(body, 0f, 0f, 54f, 8f);
            inspector = AtlasTheme.ScrollArea(body, out inspectorScroll, 8f, new RectOffset(12, 12, 0, 16));
        }

        void RefreshProps()
        {
            inspectorDirty = true;
        }

        Transform Sec(string title, string icon, bool open = true)
        {
            bool o;
            if (!sectionOpen.TryGetValue(title, out o)) o = open;
            string key = title;
            return AtlasTheme.Section(inspector, title, icon, o, on => sectionOpen[key] = on);
        }

        Text Hint(Transform parent, string text)
        {
            var t = AtlasTheme.Label(parent, text, 14, AtlasTheme.TextDim);
            return t;
        }

        void BuildInspector()
        {
            if (inspector == null) return;
            inspectorDirty = false;
            AtlasTheme.Clear(inspector);
            for (int i = 0; i < tf.Length; i++) tf[i] = null;
            nameField = null;
            if (selected == null)
            {
                propsTitle.text = "ESCENA";
                BuildSceneInspector();
            }
            else
            {
                propsTitle.text = "PROPIEDADES";
                BuildObjectInspector(selected);
            }
        }

        // ------------------------------------------------------------------
        // Selected object

        void BuildObjectInspector(AtlasItem it)
        {
            // Header: icon, name, kind and the eye / lock / copy / delete.
            var head = AtlasTheme.Box(inspector, "Encabezado", AtlasTheme.Panel2, 14);
            AtlasTheme.Column(head.gameObject, 8f, new RectOffset(10, 10, 10, 10));
            var r1 = AtlasTheme.Rect(head.transform, "Nombre").gameObject;
            AtlasTheme.Row(r1, 8f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(r1, 44f);
            var ic = AtlasTheme.IconImage(r1.transform, KindIcon(it.kind), 28f, AtlasTheme.AccentLight);
            AtlasTheme.Le(ic.gameObject, 28f, 28f);
            nameField = AtlasTheme.TextField(r1.transform, it.gameObject.name, s =>
            {
                if (selected == null || string.IsNullOrEmpty(s.Trim())) return;
                selected.gameObject.name = s.Trim();
                RefreshMode();
                Commit(true);
            });
            var r2 = AtlasTheme.Rect(head.transform, "Acciones").gameObject;
            AtlasTheme.Row(r2, 4f, new RectOffset(0, 0, 0, 0));
            AtlasTheme.Le(r2, 44f);
            string kindText = DisplayName(it.kind) + (it.Animated ? " · animado" : "");
            var kl = AtlasTheme.Label(r2.transform, kindText, 15, AtlasTheme.TextDim);
            AtlasTheme.Le(kl.gameObject, 44f, -1f, 1f);
            Button eye = null, lockB = null;
            eye = AtlasTheme.IconButton(r2.transform, it.visible ? "eye" : "eye_off", () =>
            {
                it.visible = !it.visible;
                ApplyLook(it);
                AtlasTheme.SetButtonIcon(eye, it.visible ? "eye" : "eye_off");
                Commit(true);
                Toast(it.visible ? "Visible" : "Oculto (sigue en la lista de la escena)");
            }, 44f, 22f);
            lockB = AtlasTheme.IconButton(r2.transform, it.locked ? "lock" : "unlock", () =>
            {
                it.locked = !it.locked;
                AtlasTheme.SetButtonIcon(lockB, it.locked ? "lock" : "unlock");
                AtlasTheme.SetActive(lockB, it.locked);
                ShowGizmo();
                RefreshMode();
                Commit(true);
                Toast(it.locked ? "Bloqueado: no se mueve ni se selecciona en la vista" : "Desbloqueado");
            }, 44f, 22f);
            AtlasTheme.SetActive(lockB, it.locked);
            AtlasTheme.IconButton(r2.transform, "duplicate", DuplicateSelected, 44f, 22f);
            var del = AtlasTheme.IconButton(r2.transform, "delete", DeleteSelected, 44f, 22f);
            foreach (var ri in del.GetComponentsInChildren<RawImage>()) ri.color = AtlasTheme.Danger;

            BuildTransformSection(it);
            if (it.IsLight) BuildLightSection(it);
            else if (it.kind == "model") BuildModelSection(it);
            else BuildMaterialSection(it);
            BuildKeysSection(it);
            BuildInfoSection(it);
        }

        void BuildTransformSection(AtlasItem it)
        {
            var box = Sec("Transformación", "move");
            string[] rows = { "Posición", "Rotación", "Escala" };
            Color[] ac = { AtlasTheme.AxisX, AtlasTheme.AxisY, AtlasTheme.AxisZ };
            string[] an = { "X", "Y", "Z" };
            for (int row = 0; row < 3; row++)
            {
                var line = AtlasTheme.FieldRow(box, rows[row], 70f, 40f);
                for (int c = 0; c < 3; c++)
                {
                    var al = AtlasTheme.Label(line, an[c], 14, ac[c], true, TextAnchor.MiddleCenter);
                    AtlasTheme.Le(al.gameObject, 40f, 12f);
                    int idx = row * 3 + c;
                    tf[idx] = AtlasTheme.NumberField(line, 0f, v => SetTransformValue(idx, v), 66f);
                }
            }
            FillTransformFields();
            var g = AtlasTheme.Grid(box, 3, InnerWidth, 38f);
            AtlasTheme.Chip(g, "Al suelo", () =>
            {
                var rs = it.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) return;
                Bounds b = rs[0].bounds;
                foreach (var rr in rs) b.Encapsulate(rr.bounds);
                it.transform.position += Vector3.up * -b.min.y;
                AutoKey();
                Commit(true);
            });
            AtlasTheme.Chip(g, "Sin giro", () =>
            {
                it.transform.rotation = Quaternion.identity;
                AutoKey();
                Commit(true);
            });
            AtlasTheme.Chip(g, "Escala 1", () =>
            {
                it.transform.localScale = it.IsLight ? Vector3.one * 0.25f : Vector3.one;
                AutoKey();
                Commit(true);
            });
        }

        void SetTransformValue(int idx, float v)
        {
            if (selected == null) return;
            var t = selected.transform;
            int axis = idx % 3;
            if (idx < 3) { var p = t.position; p[axis] = v; t.position = p; }
            else if (idx < 6) { var e = t.eulerAngles; e[axis] = v; t.eulerAngles = e; }
            else { var s = t.localScale; s[axis] = Mathf.Max(0.001f, v); t.localScale = s; }
            AutoKey();
            Commit(true);
        }

        void FillTransformFields()
        {
            if (selected == null || tf[0] == null) return;
            var t = selected.transform;
            for (int c = 0; c < 3; c++)
            {
                AtlasTheme.SetNumber(tf[c], t.position[c]);
                AtlasTheme.SetNumber(tf[3 + c], Mathf.Round(t.eulerAngles[c] * 10f) / 10f);
                AtlasTheme.SetNumber(tf[6 + c], t.localScale[c]);
            }
        }

        /// Swatches + hue / saturation / brightness sliders for `it.color`.
        void ColorControls(Transform box, AtlasItem it, Color[] swatches)
        {
            var g = AtlasTheme.Grid(box, 8, InnerWidth, 34f, 6f);
            foreach (var c0 in swatches)
            {
                Color cc = c0;
                var sw = AtlasTheme.Box(g, "Color", cc, 8);
                var sb = sw.gameObject.AddComponent<Button>();
                sb.onClick.AddListener(() =>
                {
                    it.color = cc;
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                });
            }
            float h, s, v;
            Color.RGBToHSV(it.color, out h, out s, out v);
            float[] hsv = { h, s, v };
            AtlasTheme.SliderRow(box, "Tono", 0f, 1f, h, "0.00", x => { hsv[0] = x; SetHsv(it, hsv); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Saturación", 0f, 1f, s, "0.00", x => { hsv[1] = x; SetHsv(it, hsv); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Brillo", 0f, 1f, v, "0.00", x => { hsv[2] = x; SetHsv(it, hsv); }, () => Commit(true));
        }

        void SetHsv(AtlasItem it, float[] hsv)
        {
            Color c = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
            c.a = it.color.a;
            it.color = c;
            ApplyLook(it);
        }

        void BuildMaterialSection(AtlasItem it)
        {
            var box = Sec("Material", "material");
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 38f);
            foreach (var p0 in MatPresets)
            {
                var p = p0;
                AtlasTheme.Chip(g, p.name, () =>
                {
                    if (!p.keepColor) it.color = p.color;
                    it.metallic = p.metallic;
                    it.smoothness = 1f - p.roughness;
                    it.emission = p.emission;
                    it.opacity = p.opacity;
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                    Toast("Material: " + p.name);
                });
            }
            var cl = AtlasTheme.Label(box, "Color base", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(cl.gameObject, 24f);
            ColorControls(box, it, Swatches);
            AtlasTheme.SliderRow(box, "Metálico", 0f, 1f, it.metallic, "0.00", x => { it.metallic = x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Rugosidad", 0f, 1f, 1f - it.smoothness, "0.00", x => { it.smoothness = 1f - x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Emisión", 0f, 8f, it.emission, "0.0", x => { it.emission = x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.SliderRow(box, "Opacidad", 0.05f, 1f, it.opacity, "0.00", x => { it.opacity = x; ApplyLook(it); }, () => Commit(true));
            if (string.IsNullOrEmpty(it.texture))
            {
                AtlasTheme.TextButton(box, "image", "Poner textura (PNG/JPG)…", () => PickTexture(it), false, 44f, 16);
            }
            else
            {
                var tl = AtlasTheme.Label(box, "Textura: " + it.texture, 14, AtlasTheme.TextDim);
                AtlasTheme.Le(tl.gameObject, 22f);
                var row = AtlasTheme.Grid(box, 2, InnerWidth, 42f);
                AtlasTheme.TextButton(row, "image", "Cambiar…", () => PickTexture(it), false, 42f, 16);
                AtlasTheme.TextButton(row, "close", "Quitar", () =>
                {
                    it.texture = "";
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                }, false, 42f, 16);
            }
        }

        void BuildLightSection(AtlasItem it)
        {
            string kind = it.kind == "spot" ? "Foco" : it.kind == "sun" ? "Sol (direccional)" : "Puntual";
            var box = Sec("Luz · " + kind, it.kind == "light" ? "light" : it.kind);
            Color[] lightColors =
            {
                Color.white, Mathf.CorrelatedColorTemperatureToRGB(3000f), Mathf.CorrelatedColorTemperatureToRGB(8000f), AtlasTheme.Hex("e5484d"),
                AtlasTheme.Hex("f5a524"), AtlasTheme.Hex("46c46e"), AtlasTheme.Hex("3d8bfd"), AtlasTheme.Hex("b25cff"),
            };
            ColorControls(box, it, lightColors);
            var tl = AtlasTheme.Label(box, "Temperatura", 15, AtlasTheme.TextColor, true);
            AtlasTheme.Le(tl.gameObject, 24f);
            var g = AtlasTheme.Grid(box, 5, InnerWidth, 36f, 4f);
            string[] names = { "Vela", "Cálida", "Día", "Nublado", "Cielo" };
            float[] kelvin = { 1900f, 3000f, 6500f, 8000f, 11000f };
            for (int i = 0; i < names.Length; i++)
            {
                float k = kelvin[i];
                var b = AtlasTheme.Chip(g, names[i], () =>
                {
                    it.color = Mathf.CorrelatedColorTemperatureToRGB(k);
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                });
                b.GetComponentInChildren<Text>().color = Mathf.CorrelatedColorTemperatureToRGB(k);
            }
            float maxI = it.kind == "sun" ? 6f : 60f;
            AtlasTheme.SliderRow(box, "Intensidad", 0f, maxI, it.lightIntensity, "0.0", x => { it.lightIntensity = x; ApplyLook(it); }, () => Commit(true));
            if (it.kind != "sun")
                AtlasTheme.SliderRow(box, "Alcance", 0.5f, 60f, it.lightRange, "0.0", x => { it.lightRange = x; ApplyLook(it); }, () => Commit(true));
            if (it.kind == "spot")
                AtlasTheme.SliderRow(box, "Ángulo", 5f, 170f, it.spotAngle, "0", x => { it.spotAngle = x; ApplyLook(it); }, () => Commit(true));
            AtlasTheme.Switch(box, "Proyecta sombras", it.shadows, on => { it.shadows = on; ApplyLook(it); Commit(true); });
            if (it.kind != "light")
                Hint(box, "Gira la luz con la herramienta Girar para apuntarla.");
        }

        void BuildModelSection(AtlasItem it)
        {
            var box = Sec("Animación del modelo", "film");
            var clips = it.ClipNames();
            if (clips.Count == 0)
            {
                Hint(box, "Este modelo no trae animaciones (o todavía se está cargando).");
            }
            else
            {
                var g = AtlasTheme.Grid(box, 2, InnerWidth, 40f);
                AtlasTheme.Chip(g, "Ninguna", () =>
                {
                    it.clip = "";
                    Commit(true);
                    RefreshProps();
                }, string.IsNullOrEmpty(it.clip));
                foreach (var c0 in clips)
                {
                    string c = c0;
                    AtlasTheme.Chip(g, c, () =>
                    {
                        it.clip = c;
                        it.SampleClip(frame / fps);
                        Commit(true);
                        RefreshProps();
                        Toast("«" + c + "» suena con la línea de tiempo: pulsa Animar ▶");
                    }, it.clip == c);
                }
                Hint(box, "La animación elegida se reproduce con la línea de tiempo y sale en el render.");
            }
            var mbox = Sec("Modelo", "import", false);
            Hint(mbox, "Los modelos importados conservan sus propios materiales y texturas.");
            var tl = AtlasTheme.Label(mbox, "Archivo: " + it.modelFile, 14, AtlasTheme.TextDim);
            AtlasTheme.Le(tl.gameObject, 22f);
        }

        void BuildKeysSection(AtlasItem it)
        {
            var box = Sec("Claves de animación", "key", false);
            int n = it.keys.Count;
            Hint(box, n == 0 ? "Sin claves. Abre «Animar», elige un fotograma, colócalo y pulsa la clave amarilla."
                             : n + (n == 1 ? " clave" : " claves") + " (fotogramas " + it.keys[0].frame + " a " + it.keys[n - 1].frame + ").");
            var g = AtlasTheme.Grid(box, 2, InnerWidth, 42f);
            AtlasTheme.TextButton(g, "key_fill", "Clave aquí", InsertKey, false, 42f, 16);
            AtlasTheme.TextButton(g, "delete", "Borrar todas", () =>
            {
                if (it.keys.Count == 0) return;
                it.keys.Clear();
                Commit(true);
                RefreshKeys();
                RefreshProps();
                Toast("Claves borradas (Deshacer las recupera)");
            }, false, 42f, 16);
        }

        void BuildInfoSection(AtlasItem it)
        {
            var box = Sec("Información", "info", false);
            int verts = 0, tris = 0;
            foreach (var mf in it.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.gameObject.layer == Gizmo.Layer) continue;
                verts += mf.sharedMesh.vertexCount;
                tris += TriCount(mf.sharedMesh);
            }
            foreach (var sm in it.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (sm.sharedMesh == null) continue;
                verts += sm.sharedMesh.vertexCount;
                tris += TriCount(sm.sharedMesh);
            }
            var rs = it.GetComponentsInChildren<Renderer>();
            string size = "-";
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                size = b.size.x.ToString("0.00") + " × " + b.size.y.ToString("0.00") + " × " + b.size.z.ToString("0.00") + " m";
            }
            Hint(box, "Vértices: " + verts.ToString("N0") + "\nTriángulos: " + tris.ToString("N0") + "\nTamaño: " + size);
        }

        static int TriCount(Mesh m)
        {
            long n = 0;
            for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s);
            return (int)(n / 3);
        }

        // ------------------------------------------------------------------
        // Nothing selected: the scene

        void BuildSceneInspector()
        {
            var head = AtlasTheme.Box(inspector, "Encabezado", AtlasTheme.Panel2, 14);
            AtlasTheme.Row(head.gameObject, 10f, new RectOffset(12, 12, 8, 8));
            AtlasTheme.Le(head.gameObject, 56f);
            var ic = AtlasTheme.IconImage(head.transform, "folder", 26f, AtlasTheme.AccentLight);
            AtlasTheme.Le(ic.gameObject, 26f, 26f);
            var pn = AtlasTheme.Label(head.transform, store != null ? store.name : "", 18, AtlasTheme.TextColor, true);
            AtlasTheme.Le(pn.gameObject, 40f, -1f, 1f);
            var cnt = AtlasTheme.Label(head.transform, items.Count + (items.Count == 1 ? " objeto" : " objetos"), 14, AtlasTheme.TextDim, false, TextAnchor.MiddleRight);
            cnt.horizontalOverflow = HorizontalWrapMode.Overflow;
            AtlasTheme.Le(cnt.gameObject, 40f, 90f);

            var amb = Sec("Ambiente rápido (un toque)", "sparkle");
            var g = AtlasTheme.Grid(amb, 2, InnerWidth, 40f);
            foreach (var p0 in Ambience.Presets)
            {
                var p = p0;
                AtlasTheme.Chip(g, p.name, () =>
                {
                    Ambience.Reset(env, p.id);
                    ApplyEnv();
                    Commit(true);
                    RefreshProps();
                    Toast("Ambiente: " + p.name);
                }, env.preset == p.id);
            }

            var fx = Sec("Efectos de imagen", "image");
            AtlasTheme.SliderRow(fx, "Exposición", -2f, 2f, env.exposure, "0.00", x => { env.exposure = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Contraste", -50f, 50f, env.contrast, "0", x => { env.contrast = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Saturación", -100f, 100f, env.saturation, "0", x => { env.saturation = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Resplandor", 0f, 1f, env.bloom, "0.00", x => { env.bloom = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Viñeta", 0f, 1f, env.vignette, "0.00", x => { env.vignette = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Grano", 0f, 1f, env.grain, "0.00", x => { env.grain = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.SliderRow(fx, "Niebla", 0f, 0.1f, env.fog, "0.000", x => { env.fog = x; ApplyEnv(); }, () => Commit(true));
            AtlasTheme.TextButton(fx, "undo", "Valores del ambiente", () =>
            {
                Ambience.Reset(env, env.preset);
                ApplyEnv();
                Commit(true);
                RefreshProps();
            }, false, 42f, 16);

            var view = Sec("Vista", "view", false);
            AtlasTheme.Switch(view, "Suelo con cuadrícula", ground.activeSelf, on =>
            {
                ground.SetActive(on);
                AtlasTheme.SetButtonIcon(eyeBtn, on ? "eye" : "eye_off");
                Commit(true);
            });
            AtlasTheme.SliderRow(view, "Campo visual", 20f, 100f, cam.fieldOfView, "0", x => cam.fieldOfView = x, null);

            var list = Sec("Objetos de la escena", "select");
            if (items.Count == 0) Hint(list, "La escena está vacía. Añade formas, luces o importa un modelo con el botón +.");
            foreach (var it0 in items)
            {
                var it = it0;
                if (it == null) continue;
                var row = AtlasTheme.Box(list, "Objeto", AtlasTheme.Panel2, 10);
                AtlasTheme.Le(row.gameObject, 46f);
                AtlasTheme.Row(row.gameObject, 8f, new RectOffset(10, 4, 0, 0));
                var pick = row.gameObject.AddComponent<Button>();
                pick.onClick.AddListener(() => Select(it));
                var oi = AtlasTheme.IconImage(row.transform, KindIcon(it.kind), 22f, it.visible ? AtlasTheme.AccentLight : AtlasTheme.TextDim);
                AtlasTheme.Le(oi.gameObject, 22f, 22f);
                var nl = AtlasTheme.Label(row.transform, it.gameObject.name, 16, it.visible ? AtlasTheme.TextColor : AtlasTheme.TextDim, true);
                AtlasTheme.Le(nl.gameObject, 46f, -1f, 1f);
                if (it.locked)
                {
                    var li = AtlasTheme.IconImage(row.transform, "lock", 18f, AtlasTheme.TextDim);
                    AtlasTheme.Le(li.gameObject, 18f, 18f);
                }
                Button eb = null;
                eb = AtlasTheme.IconButton(row.transform, it.visible ? "eye" : "eye_off", () =>
                {
                    it.visible = !it.visible;
                    ApplyLook(it);
                    Commit(true);
                    RefreshProps();
                }, 40f, 20f);
            }
        }
    }
}
