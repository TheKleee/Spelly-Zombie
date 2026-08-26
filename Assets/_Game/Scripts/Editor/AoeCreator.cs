using UnityEditor;
using UnityEngine;

namespace SpellyZombie
{
    /// ★ THE AREA WINDOW. Opened from the Spell Creator when you pick "create
    /// new area" or open an existing one. It has its own preview and its own
    /// save.
    ///
    /// An area has no numbers of its own - it works from the spell carrying it.
    /// What you set here is only: where it appears, whether it spreads, and
    /// what it looks like.
    public class AoeCreator : EditorWindow
    {
        SpellBook _book;
        AoeDef _aoe;
        SpellDef _owner;
        readonly SpellPreview _preview = new SpellPreview();
        GameObject _shownPrefab;

        public static void Show(SpellBook book, AoeDef aoe, SpellDef owner)
        {
            var w = GetWindow<AoeCreator>("Area");
            w.minSize = new Vector2(720, 440);
            w._book = book;
            w._aoe = aoe;
            w._owner = owner;
            w._shownPrefab = null;
            w.Focus();
        }

        void OnEnable() => _preview.OnNeedsRepaint = Repaint;
        void OnDisable() => _preview.Dispose();

        void OnGUI()
        {
            if (_aoe == null)
            {
                EditorGUILayout.HelpBox("Open an area from the Spell Creator.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // ---- left: what it looks like, rendered ----------------------
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(340)))
                {
                    EditorGUILayout.LabelField("PREVIEW", EditorStyles.boldLabel);
                    var rect = GUILayoutUtility.GetRect(340, 340, GUILayout.ExpandWidth(false));
                    if (_shownPrefab != _aoe.Prefab)
                    {
                        _preview.Show(_aoe.Prefab);
                        _shownPrefab = _aoe.Prefab;
                    }
                    if (_owner != null)
                    {
                        var pay = _owner.Payload;
                        _preview.Tint(pay.Tint(), SpellPayload.StateT01(pay.State), _owner.Skin);
                    }
                    _preview.Draw(rect, posable: true);
                    EditorGUILayout.LabelField("Coloured by the spell that carries it.",
                        EditorStyles.wordWrappedMiniLabel);
                }

                // ---- right: the few things an area owns ----------------------
                using (new EditorGUILayout.VerticalScope())
                {
                    _aoe.Name = EditorGUILayout.TextField("Name", _aoe.Name);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("LOOK", EditorStyles.boldLabel);
                    _aoe.Prefab = (GameObject)EditorGUILayout.ObjectField(
                        "Prefab", _aoe.Prefab, typeof(GameObject), false);
                    EditorGUILayout.LabelField("A fire effect, a lightning effect, a rock, a trail. Anything.",
                        EditorStyles.wordWrappedMiniLabel);
                    _aoe.TrailWidth = EditorGUILayout.Slider("Trail width", _aoe.TrailWidth, 0f, 1f);
                    _aoe.TrailSeconds = EditorGUILayout.Slider("Trail lasts", _aoe.TrailSeconds, 0f, 20f);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("WHERE IT STARTS", EditorStyles.boldLabel);
                    _aoe.Offset = EditorGUILayout.Vector3Field("Offset from the spell", _aoe.Offset);
                    EditorGUILayout.LabelField("It rushes back to the spell from here. Put Y at 20 and it " +
                                               "falls from the sky. Leave it at zero and it sits on the spell.",
                        EditorStyles.wordWrappedMiniLabel);

                    EditorGUILayout.Space();
                    _aoe.Spreading = EditorGUILayout.Toggle("Spreading", _aoe.Spreading);
                    EditorGUILayout.LabelField("Appears again on nearby things that meet the same condition.",
                        EditorStyles.wordWrappedMiniLabel);

                    GUILayout.FlexibleSpace();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("SAVE", GUILayout.Height(28)) && _book != null)
                        {
                            _book.Save();
                            ShowNotification(new GUIContent("Saved"));
                        }
                        if (GUILayout.Button("DELETE", GUILayout.Height(28), GUILayout.Width(90))
                            && _book != null)
                            DeleteArea();
                    }
                }
            }
        }

        /// Gone from the book, and every spell that carried it now carries
        /// nothing - said in the dialog, not discovered later.
        void DeleteArea()
        {
            int carriers = 0;
            foreach (var sp in _book.spells) if (sp.Aoe == _aoe.Name) carriers++;
            string warning = carriers > 0
                ? $"Delete '{_aoe.Name}'? {carriers} spell(s) carry it and will carry nothing."
                : $"Delete '{_aoe.Name}'?";
            if (!EditorUtility.DisplayDialog("Delete area", warning, "Delete", "Keep")) return;

            _book.aoes.Remove(_aoe);
            foreach (var sp in _book.spells)
                if (sp.Aoe == _aoe.Name) sp.Aoe = "";
            _book.Save();
            if (carriers > 0)
                Debug.Log($"[SpellyZombie] area '{_aoe.Name}' deleted - {carriers} spell(s) now carry no area.");
            _aoe = null;
            Repaint();
        }
    }
}
