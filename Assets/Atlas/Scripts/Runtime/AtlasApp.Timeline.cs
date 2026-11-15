// ATLAS 3D (Unity) - the timeline, the same as the Godot version, with
// three sizes (▾ ▴):
//   one bar      play, frame stepping, key ◆ (+ what to key), auto-key,
//                frame, duration, «more»
//   compact      + ruler and one row per object with its keys
//   expanded     + every tool (start / end, key jumps, delete, curves,
//                range, FPS, speed, loop) and one row per track
// Keys are diamonds: tap to select, drag to move, hold for the key menu.
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Atlas
{
    public partial class AtlasApp
    {
        int timelineState = 1;   // 0 bar, 1 compact, 2 expanded
        TimelineView tlView;
        RectTransform tlBarContent;
        Button playBtn, keyBtn, autoBtn, loopBtn, growBtn, shrinkBtn, moreBtn;
        Text timeLabel, speedLabel;
        InputField frameField, startField, endField, fpsField;
        readonly List<GameObject> tlAdvanced = new List<GameObject>();
        bool autoKey, loopPlay = true, showAllKeys;
        int startFrame;
        float speed = 1f;
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 1.5f, 2f };
        readonly HashSet<string> selKeys = new HashSet<string>();
        readonly List<CopiedKey> clipboard = new List<CopiedKey>();

        class CopiedKey
        {
            public string itemId, track;
            public int rel;
            public KeyData data;
        }

        float TimelineHeight
        {
            get { return timelineState == 0 ? 56f : timelineState == 1 ? 176f : 268f; }
        }

        static string SelKey(AtlasItem it, string track, int f) { return it.id + "|" + track + "|" + f; }

        void BuildTimeline()
        {
            var tl = AtlasTheme.Box(viewArea, "LineaDeTiempo", AtlasTheme.Panel);
            var r = tl.rectTransform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = Vector2.zero;
            timeline = tl.gameObject;

            // Top: the scrollable bar + the size buttons (always reachable).
            var top = AtlasTheme.Rect(tl.transform, "Barra");
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0f, 54f);
            top.anchoredPosition = Vector2.zero;
            var sizes = AtlasTheme.Rect(top, "Tamano");
            AtlasTheme.Pin(sizes, 1f, 0.5f, new Vector2(-4f, 0f), new Vector2(92f, 46f));
            AtlasTheme.Row(sizes.gameObject, 2f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight);
            shrinkBtn = AtlasTheme.IconButton(sizes, "chevron_down", () => SetTimelineState(timelineState - 1), 44f, 22f);
            growBtn = AtlasTheme.IconButton(sizes, "chevron_up", () => SetTimelineState(timelineState + 1), 44f, 22f);
            var scrollHolder = AtlasTheme.Rect(top, "Desplazable");
            AtlasTheme.Fill(scrollHolder, 6f, 98f, 4f, 4f);
            BuildTimelineBar(scrollHolder);

            // Tracks
            var viewRt = AtlasTheme.Rect(tl.transform, "Pistas");
            AtlasTheme.Fill(viewRt, 0f, 0f, 56f, 0f);
            viewRt.gameObject.AddComponent<RectMask2D>();
            tlView = viewRt.gameObject.AddComponent<TimelineView>();
            tlView.isSelected = (row, f) => row.item != null && selKeys.Contains(SelKey(row.item, row.track, f));
            tlView.onScrub = f =>
            {
                if (playing) TogglePlay();
                SetFrame(f);
            };
            tlView.onTapKey = TapKey;
            tlView.onTapEmpty = f =>
            {
                selKeys.Clear();
                if (playing) TogglePlay();
                SetFrame(Mathf.Clamp(f, startFrame, endFrame));
                RefreshKeys();
            };
            tlView.onMoveSelected = MoveSelectedKeys;
            tlView.onKeyMenu = (row, f, sp) =>
            {
                if (!selKeys.Contains(SelKey(row.item, row.track, f))) { selKeys.Clear(); selKeys.Add(SelKey(row.item, row.track, f)); }
                RefreshKeys();
                OpenKeyMenu(sp);
            };

            SetTimelineState(1);
        }

        void BuildTimelineBar(RectTransform holder)
        {
            var view = AtlasTheme.Rect(holder, "Vista");
            AtlasTheme.Fill(view);
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);
            view.gameObject.AddComponent<RectMask2D>();
            var content = AtlasTheme.Rect(view, "Herramientas");
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = Vector2.zero;
            AtlasTheme.Row(content.gameObject, 3f, new RectOffset(0, 8, 0, 0));
            content.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var sr = holder.gameObject.AddComponent<ScrollRect>();
            sr.viewport = view;
            sr.content = content;
            sr.horizontal = true;
            sr.vertical = false;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = true;
            tlBarContent = content;
            Transform bar = content;

            // Essentials (every size), most used first.
            playBtn = AtlasTheme.IconButton(bar, "play", TogglePlay, 52f, 24f);
            var pimg = playBtn.GetComponent<Image>();
            pimg.color = AtlasTheme.AccentDim;
            pimg.sprite = AtlasTheme.Rounded(19);
            AtlasTheme.Le(playBtn.gameObject, 40f, 54f);
            AtlasTheme.IconButton(bar, "step_back", () => StepFrame(-1), 42f, 22f);
            AtlasTheme.IconButton(bar, "step_fwd", () => StepFrame(1), 42f, 22f);
            keyBtn = AtlasTheme.IconButton(bar, "key_fill", () => InsertKeyOf("default"), 42f, 24f);
            Button insMore = null;
            insMore = AtlasTheme.IconButton(bar, "chevron_down", () => OpenInsertMenu(insMore.transform.position), 30f, 16f);
            autoBtn = AtlasTheme.IconButton(bar, "record", () =>
            {
                autoKey = !autoKey;
                AtlasTheme.SetActive(autoBtn, autoKey);
                if (autoKey) foreach (var ri in autoBtn.GetComponentsInChildren<RawImage>()) ri.color = AtlasTheme.Danger;
                Toast(autoKey ? "Clave automática: cada cambio crea una clave" : "Clave automática desactivada");
            }, 42f, 22f);
            frameField = AtlasTheme.NumberField(bar, 0f, v =>
            {
                if (playing) TogglePlay();
                int f = Mathf.Max(0, Mathf.RoundToInt(v));
                if (f > endFrame)
                {
                    endFrame = f;
                    Toast("Animación alargada hasta el fotograma " + f);
                    Commit(true);
                }
                SetFrame(f);
            }, 64f);
            var tb = AtlasTheme.TextButton(bar, null, "0.0 / 5.0 s", AskDuration, false, 42f, 15, 112f);
            tb.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            timeLabel = tb.GetComponentInChildren<Text>();
            Button mb = null;
            mb = AtlasTheme.IconButton(bar, "more", () => OpenOptionsMenu(mb.transform.position), 42f, 22f);
            moreBtn = mb;

            // Advanced (expanded only)
            Adv(AtlasTheme.Separator(bar, true));
            Adv(AtlasTheme.IconButton(bar, "to_start", () => { if (playing) TogglePlay(); SetFrame(startFrame); }, 42f, 22f).gameObject);
            Adv(AtlasTheme.IconButton(bar, "key_prev", () => JumpKey(-1), 42f, 22f).gameObject);
            Adv(AtlasTheme.IconButton(bar, "key_next", () => JumpKey(1), 42f, 22f).gameObject);
            Adv(AtlasTheme.IconButton(bar, "to_end", () => { if (playing) TogglePlay(); SetFrame(endFrame); }, 42f, 22f).gameObject);
            Adv(AtlasTheme.IconButton(bar, "delete", DeleteKeys, 42f, 22f).gameObject);
            Button curve = null;
            curve = AtlasTheme.IconButton(bar, "curve", () => OpenKeyMenu(curve.transform.position), 42f, 22f);
            Adv(curve.gameObject);
            Adv(AtlasTheme.Separator(bar, true));
            Adv(SmallLabel(bar, "Inicio"));
            startField = AtlasTheme.NumberField(bar, 0f, v => SetRange(Mathf.RoundToInt(v), endFrame), 58f);
            Adv(startField.gameObject);
            Adv(SmallLabel(bar, "Fin"));
            endField = AtlasTheme.NumberField(bar, endFrame, v => SetRange(startFrame, Mathf.RoundToInt(v)), 58f);
            Adv(endField.gameObject);
            Adv(SmallLabel(bar, "FPS"));
            fpsField = AtlasTheme.NumberField(bar, fps, v =>
            {
                fps = Mathf.Clamp(Mathf.RoundToInt(v), 1, 240);
                Commit(true);
                RefreshTimeline();
            }, 52f);
            Adv(fpsField.gameObject);
            var sp = AtlasTheme.TextButton(bar, null, "1x", () =>
            {
                int i = System.Array.IndexOf(Speeds, speed);
                speed = Speeds[(i + 1) % Speeds.Length];
                speedLabel.text = SpeedText();
            }, false, 40f, 15, 58f);
            speedLabel = sp.GetComponentInChildren<Text>();
            Adv(sp.gameObject);
            loopBtn = AtlasTheme.IconButton(bar, "loop", () =>
            {
                loopPlay = !loopPlay;
                AtlasTheme.SetActive(loopBtn, loopPlay);
            }, 42f, 22f);
            AtlasTheme.SetActive(loopBtn, loopPlay);
            Adv(loopBtn.gameObject);
        }

        string SpeedText()
        {
            return (speed == Mathf.Round(speed) ? speed.ToString("0") : speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) + "x";
        }

        GameObject SmallLabel(Transform bar, string text)
        {
            var l = AtlasTheme.Label(bar, text, 14, AtlasTheme.TextDim);
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            AtlasTheme.Le(l.gameObject, 40f, text.Length * 8f + 6f);
            return l.gameObject;
        }

        void Adv(GameObject go)
        {
            tlAdvanced.Add(go);
        }

        void SetTimelineState(int s)
        {
            timelineState = Mathf.Clamp(s, 0, 2);
            foreach (var go in tlAdvanced) go.SetActive(timelineState == 2);
            moreBtn.gameObject.SetActive(timelineState != 2);
            tlView.gameObject.SetActive(timelineState != 0);
            shrinkBtn.interactable = timelineState > 0;
            growBtn.interactable = timelineState < 2;
            var r = timeline.GetComponent<RectTransform>();
            r.sizeDelta = new Vector2(0f, TimelineHeight);
            LayoutTimelineNeighbours();
            tlView.Fit();
            RefreshKeys();
        }

        /// The panels above the timeline make room for it.
        void LayoutTimelineNeighbours()
        {
            if (timeline == null || toastHolder == null || propsPanel == null) return;
            bool on = timeline.activeSelf;
            float h = on ? TimelineHeight : 0f;
            bool bar = meshBar != null && meshBar.activeSelf;
            toastHolder.anchoredPosition = new Vector2(RailWidth * 0.5f, h + (bar ? 80f : 20f));
            var pr = propsPanel.GetComponent<RectTransform>();
            pr.offsetMin = new Vector2(pr.offsetMin.x, h + 10f);
            if (meshBar != null && meshBar.activeSelf) ((RectTransform)meshBar.transform).anchoredPosition = new Vector2(0f, h + 10f);
        }

        void ToggleTimeline()
        {
            timeline.SetActive(!timeline.activeSelf);
            AtlasTheme.SetActive(animBtn, timeline.activeSelf);
            LayoutTimelineNeighbours();
            if (timeline.activeSelf)
            {
                tlView.Fit();
                RefreshKeys();
                Toast("Animar: elige un fotograma, coloca el objeto y pulsa la clave ◆");
            }
            else if (playing) TogglePlay();
        }

        // ------------------------------------------------------------------
        // State on screen

        void RefreshTimeline()
        {
            if (tlView == null) return;
            AtlasTheme.SetNumber(frameField, Mathf.Round(frame));
            float len = (endFrame - startFrame) / (float)fps;
            timeLabel.text = ((frame - startFrame) / fps).ToString("0.0") + " / " + len.ToString("0.0") + " s";
            AtlasTheme.SetNumber(startField, startFrame);
            AtlasTheme.SetNumber(endField, endFrame);
            AtlasTheme.SetNumber(fpsField, fps);
            AtlasTheme.SetButtonIcon(playBtn, playing ? "pause" : "play");
            bool here = selected != null && (poseMode && activeBone != "" ? selected.TrackFrames("b:" + activeBone).Contains(Mathf.RoundToInt(frame)) : selected.HasKey(Mathf.RoundToInt(frame)));
            foreach (var ri in keyBtn.GetComponentsInChildren<RawImage>()) ri.color = here ? AtlasTheme.Key : new Color(1f, 0.85f, 0.55f);
            tlView.frame = frame;
            tlView.startFrame = startFrame;
            tlView.endFrame = endFrame;
            tlView.playing = playing;
            if (timeline.activeSelf) tlView.Refresh();
        }

        /// Rebuilds the rows of the track view (keys changed).
        void RefreshKeys()
        {
            if (tlView == null) return;
            var rows = new List<TimelineRow>();
            var ids = new List<AtlasItem>();
            if (selected != null && !showAllKeys) ids.Add(selected);
            else foreach (var it in items) if (it != null && it.Animated) ids.Add(it);
            string focus = poseMode && selected != null && activeBone != "" ? "b:" + activeBone : "";
            if (focus != "")
                rows.Add(new TimelineRow { item = selected, track = focus, label = BoneNames.Display(BoneNames.Leaf(activeBone)), focus = true, frames = selected.TrackFrames(focus) });
            foreach (var it in ids)
            {
                rows.Add(new TimelineRow { item = it, track = "", label = (focus != "" && it == selected ? "Todo: " : "") + it.gameObject.name, frames = it.TrackFrames("") });
                if (timelineState < 2) continue;
                foreach (var track in it.Tracks())
                {
                    if (it == selected && track == focus) continue;
                    rows.Add(new TimelineRow { item = it, track = track, label = "   " + TrackName(track), frames = it.TrackFrames(track) });
                }
            }
            float a0, a1;
            if (AudioBar(out a0, out a1))
                rows.Insert(0, new TimelineRow { item = null, track = "audio", label = "♪ " + Path.GetFileNameWithoutExtension(audioFile), barStart = a0, barEnd = a1 });
            tlView.rows = rows;
            RefreshTimeline();
        }

        static string TrackName(string track)
        {
            switch (track)
            {
                case "T": return "Posición";
                case "R": return "Rotación";
                case "S": return "Escala";
                case "U": return "Textura (UV)";
            }
            return track.StartsWith("b:") ? BoneNames.Display(BoneNames.Leaf(track.Substring(2))) : track;
        }

        // ------------------------------------------------------------------
        // Playing and moving in time

        void StepFrame(int d)
        {
            if (playing) TogglePlay();
            SetFrame(Mathf.Clamp(Mathf.Round(frame) + d, 0f, endFrame));
        }

        void SetRange(int s, int e)
        {
            startFrame = Mathf.Clamp(s, 0, 100000);
            endFrame = Mathf.Clamp(e, startFrame + 1, 600 * fps);
            if (frame > endFrame) SetFrame(endFrame);
            tlView.Fit();
            Commit(true);
            RefreshKeys();
        }

        void AskDuration()
        {
            AtlasTheme.Dialog(canvasRoot, "Duración de la animación", "Escribe los segundos (por ejemplo 10s) o los fotogramas (por ejemplo 240).",
                ((endFrame - startFrame) / (float)fps).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "s", "Aplicar", false, txt =>
            {
                string t = txt.Trim().ToLowerInvariant().Replace(',', '.');
                int frames = -1;
                float v;
                if (t.EndsWith("s"))
                {
                    if (float.TryParse(t.TrimEnd('s').Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                        frames = Mathf.RoundToInt(v * fps);
                }
                else if (float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    frames = Mathf.RoundToInt(v);
                if (frames < 1) { Toast("No entendí la duración: usa por ejemplo 10s o 240", true); return; }
                SetRange(startFrame, startFrame + frames);
                Toast("La animación dura " + (frames / (float)fps).ToString("0.0") + " s (" + frames + " fotogramas)");
            });
            toastHolder.SetAsLastSibling();
        }

        // ------------------------------------------------------------------
        // Keys

        void InsertKey()
        {
            InsertKeyOf("default");
        }

        void InsertKeyOf(string which)
        {
            if (selected == null) { Toast("Selecciona un objeto para guardar su clave"); return; }
            int f = Mathf.RoundToInt(frame);
            string what;
            switch (which)
            {
                case "all": selected.SetKey(f, 7, new List<string>()); what = "posición, rotación y escala"; break;
                case "position": selected.SetKey(f, AtlasItem.ChPos, new List<string>()); what = "posición"; break;
                case "rotation": selected.SetKey(f, AtlasItem.ChRot, new List<string>()); what = "rotación"; break;
                case "scale": selected.SetKey(f, AtlasItem.ChScale, new List<string>()); what = "escala"; break;
                case "bone":
                    if (!poseMode || activeBone == "") { Toast("Entra en modo pose y elige un hueso"); return; }
                    selected.RecordBone(activeBone);
                    selected.SetKey(f, 0, new List<string> { activeBone });
                    what = BoneNames.Display(BoneNames.Leaf(activeBone));
                    break;
                case "pose_all":
                    if (selected.pose.Count == 0) { Toast("Este modelo no tiene huesos con pose"); return; }
                    selected.SetKey(f, 0, null);
                    what = "todos los huesos con pose";
                    break;
                default:
                    if (poseMode && activeBone != "")
                    {
                        selected.RecordBone(activeBone);
                        selected.SetKey(f, 0, new List<string> { activeBone });
                        what = BoneNames.Display(BoneNames.Leaf(activeBone));
                    }
                    else
                    {
                        selected.SetKey(f);
                        what = selected.pose.Count > 0 ? "transformación y pose" : "transformación";
                    }
                    break;
            }
            Commit(true);
            RefreshKeys();
            RefreshProps();
            Toast("Clave (" + what + ") en el fotograma " + f);
        }

        void OpenInsertMenu(Vector3 worldPos)
        {
            var l = new List<AtlasTheme.MenuItem>
            {
                AtlasTheme.MenuItem.Header("Guardar clave de"),
                new AtlasTheme.MenuItem("key_fill", "Posición, rotación y escala", () => InsertKeyOf("all")),
                new AtlasTheme.MenuItem("move", "Sólo posición", () => InsertKeyOf("position")),
                new AtlasTheme.MenuItem("rotate", "Sólo rotación", () => InsertKeyOf("rotation")),
                new AtlasTheme.MenuItem("scale", "Sólo escala", () => InsertKeyOf("scale")),
                new AtlasTheme.MenuItem("bone", "Pose: hueso activo", () => InsertKeyOf("bone")),
                new AtlasTheme.MenuItem("bone", "Pose: todos los huesos con pose", () => InsertKeyOf("pose_all")),
            };
            AtlasTheme.Popup(canvasRoot, RectTransformUtility.WorldToScreenPoint(null, worldPos), l);
            toastHolder.SetAsLastSibling();
        }

        void DeleteKey()
        {
            DeleteKeys();
        }

        /// The selected keys, or the selected object's key at this frame.
        void DeleteKeys()
        {
            int n = 0;
            if (selKeys.Count > 0)
            {
                foreach (var s in new List<string>(selKeys))
                {
                    var parts = s.Split('|');
                    var it = FindItem(parts[0]);
                    if (it == null) continue;
                    it.DeleteTrackKey(parts[1], int.Parse(parts[2]));
                    n++;
                }
                selKeys.Clear();
            }
            else if (selected != null)
            {
                int f = Mathf.RoundToInt(frame);
                if (poseMode && activeBone != "") { if (selected.TrackFrames("b:" + activeBone).Contains(f)) { selected.DeleteTrackKey("b:" + activeBone, f); n++; } }
                else if (selected.RemoveKey(f)) n++;
            }
            if (n == 0) { Toast("No hay claves en este fotograma (o selecciona algunas)"); return; }
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            RefreshProps();
            Toast(n == 1 ? "Clave borrada" : n + " claves borradas");
        }

        AtlasItem FindItem(string id)
        {
            foreach (var it in items) if (it != null && it.id == id) return it;
            return null;
        }

        void TapKey(TimelineRow row, int f)
        {
            if (row.item == null) return;
            string k = SelKey(row.item, row.track, f);
            if (selKeys.Contains(k)) selKeys.Remove(k);
            else selKeys.Add(k);
            if (playing) TogglePlay();
            SetFrame(f);
            RefreshKeys();
        }

        void MoveSelectedKeys(int delta)
        {
            if (selKeys.Count == 0 || delta == 0) return;
            var list = new List<string[]>();
            foreach (var s in selKeys) list.Add(s.Split('|'));
            // Furthest first in the direction of the move (no collisions).
            list.Sort((a, b) => delta > 0 ? int.Parse(b[2]).CompareTo(int.Parse(a[2])) : int.Parse(a[2]).CompareTo(int.Parse(b[2])));
            var next = new HashSet<string>();
            foreach (var p in list)
            {
                var it = FindItem(p[0]);
                if (it == null) continue;
                int from = int.Parse(p[2]);
                int to = Mathf.Max(0, from + delta);
                it.MoveTrackKey(p[1], from, to);
                next.Add(SelKey(it, p[1], to));
            }
            selKeys.Clear();
            foreach (var s in next) selKeys.Add(s);
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
        }

        void OpenKeyMenu(Vector3 screenOrWorld)
        {
            Vector2 sp = screenOrWorld;
            var l = new List<AtlasTheme.MenuItem> { AtlasTheme.MenuItem.Header("Interpolación (hasta la clave siguiente)") };
            for (int i = 0; i < Interp.Modes.Length; i++)
            {
                string mode = Interp.Modes[i];
                l.Add(new AtlasTheme.MenuItem("curve", Interp.Names[i], () => SetInterpSelected(mode)));
            }
            l.Add(AtlasTheme.MenuItem.Header("Claves"));
            l.Add(new AtlasTheme.MenuItem("copy", "Copiar claves", CopyKeys));
            l.Add(new AtlasTheme.MenuItem("paste", "Pegar en el fotograma " + Mathf.RoundToInt(frame), PasteKeys));
            l.Add(new AtlasTheme.MenuItem("delete", "Borrar claves", DeleteKeys));
            l.Add(new AtlasTheme.MenuItem("select", "Seleccionar todas", () =>
            {
                foreach (var row in tlView.rows) foreach (int f in row.frames) selKeys.Add(SelKey(row.item, row.track, f));
                RefreshKeys();
            }));
            l.Add(new AtlasTheme.MenuItem("deselect", "Deseleccionar claves", () => { selKeys.Clear(); RefreshKeys(); }));
            AtlasTheme.Popup(canvasRoot, sp, l);
            toastHolder.SetAsLastSibling();
        }

        void SetInterpSelected(string mode)
        {
            int n = 0;
            var targets = new List<string>(selKeys);
            if (targets.Count == 0 && selected != null && selected.HasKey(Mathf.RoundToInt(frame)))
                targets.Add(SelKey(selected, "", Mathf.RoundToInt(frame)));
            foreach (var s in targets)
            {
                var p = s.Split('|');
                var it = FindItem(p[0]);
                if (it == null) continue;
                it.SetInterp(int.Parse(p[2]), mode);
                n++;
            }
            if (n == 0) { Toast("Selecciona claves (tócalas en la línea de tiempo)"); return; }
            Commit(true);
            SetFrame(frame);
            Toast("Interpolación: " + Interp.Name(mode));
        }

        void CopyKeys()
        {
            if (selKeys.Count == 0) { Toast("Selecciona claves para copiarlas"); return; }
            clipboard.Clear();
            int min = int.MaxValue;
            foreach (var s in selKeys) min = Mathf.Min(min, int.Parse(s.Split('|')[2]));
            foreach (var s in selKeys)
            {
                var p = s.Split('|');
                var it = FindItem(p[0]);
                if (it == null) continue;
                var k = it.KeyAt(int.Parse(p[2]));
                if (k == null) continue;
                clipboard.Add(new CopiedKey { itemId = p[0], track = p[1], rel = k.frame - min, data = JsonUtility.FromJson<KeyData>(JsonUtility.ToJson(k)) });
            }
            Toast(clipboard.Count == 1 ? "Clave copiada" : clipboard.Count + " claves copiadas");
        }

        void PasteKeys()
        {
            if (clipboard.Count == 0) { Toast("No hay claves copiadas"); return; }
            int f0 = Mathf.RoundToInt(frame);
            bool oneObject = true;
            foreach (var c in clipboard) if (c.itemId != clipboard[0].itemId) oneObject = false;
            int n = 0;
            foreach (var c in clipboard)
            {
                // Copied from one object: paste onto the selected one.
                var it = oneObject && selected != null ? selected : FindItem(c.itemId);
                if (it == null) continue;
                var dst = it.GetOrAddKey(f0 + c.rel);
                if (c.track == "")
                {
                    foreach (var t in new[] { "T", "R", "S", "U" }) if (c.data.Has(t)) KeyData.CopyTrack(c.data, dst, t);
                    if (c.data.bones != null) foreach (var b in c.data.bones) KeyData.CopyTrack(c.data, dst, "b:" + b.path);
                    dst.interp = c.data.interp;
                }
                else KeyData.CopyTrack(c.data, dst, c.track);
                n++;
            }
            Commit(true);
            SetFrame(frame);
            RefreshKeys();
            Toast(n == 1 ? "Clave pegada" : n + " claves pegadas");
        }

        void OpenOptionsMenu(Vector3 worldPos)
        {
            var l = new List<AtlasTheme.MenuItem>
            {
                new AtlasTheme.MenuItem("key", "Guardar clave de…", () => OpenInsertMenu(worldPos)),
                new AtlasTheme.MenuItem("delete", "Borrar claves aquí / seleccionadas", DeleteKeys),
                new AtlasTheme.MenuItem("curve", "Interpolación, copiar y pegar…", () => OpenKeyMenu(RectTransformUtility.WorldToScreenPoint(null, worldPos))),
                new AtlasTheme.MenuItem("to_end", "Duración: " + (endFrame - startFrame) + " fotogramas (" + ((endFrame - startFrame) / (float)fps).ToString("0.0") + " s)…", AskDuration),
                new AtlasTheme.MenuItem("add", "Alargar 5 segundos", () =>
                {
                    SetRange(startFrame, endFrame + 5 * fps);
                    Toast("La animación ahora termina en el fotograma " + endFrame);
                }),
                string.IsNullOrEmpty(audioFile)
                    ? new AtlasTheme.MenuItem("music", "Añadir audio (MP3, OGG, WAV)…", PickAudio)
                    : new AtlasTheme.MenuItem("music", "Audio: empezar en el fotograma " + Mathf.RoundToInt(frame), () => { audioStart = Mathf.RoundToInt(frame); Commit(true); RefreshKeys(); }),
                AtlasTheme.MenuItem.Check("loop", "Repetir en bucle", loopPlay, () => { loopPlay = !loopPlay; AtlasTheme.SetActive(loopBtn, loopPlay); }),
                new AtlasTheme.MenuItem("close", string.IsNullOrEmpty(audioFile) ? "(sin audio)" : "Quitar el audio", () => { if (!string.IsNullOrEmpty(audioFile)) RemoveAudio(); }),
                AtlasTheme.MenuItem.Check("timeline", "Hoja de animación (todos los objetos)", showAllKeys, () =>
                {
                    showAllKeys = !showAllKeys;
                    if (showAllKeys && timelineState != 2) Toast("Hoja de animación: todas las claves de todos los objetos. Amplía (▴) para ver una pista por propiedad.");
                    RefreshKeys();
                }),
            };
            AtlasTheme.Popup(canvasRoot, RectTransformUtility.WorldToScreenPoint(null, worldPos), l);
            toastHolder.SetAsLastSibling();
        }
    }
}
