using System;
using System.Collections.Generic;
using System.Linq;
using Clpsplug.PooledObjects.Runtime;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Clpsplug.PooledObjects.Editor
{
    public class PoolInspector : EditorWindow
    {
        [SerializeField] private TreeViewState state;
        private PoolTableView _tableView;

        private bool _autoRefresh = true;

        private const string EditorPrefKey = "com.exploding-cable.pool-inspector";

        [MenuItem("Tools/ClpsPLUG/Poolable Objects/Inspector")]
        private static void Open(MenuCommand command)
        {
            var window = GetWindow<PoolInspector>();
            window.titleContent = new GUIContent("Object pool inspector");
        }

        private void OnEnable()
        {
            FromSavedState(JsonUtility.FromJson<InspectorState>(
                EditorPrefs.GetString(EditorPrefKey, JsonUtility.ToJson(new InspectorState()))
            ));
            _tableView ??= new PoolTableView(state ?? new TreeViewState());
        }

        private InspectorState ToSavedState()
        {
            return new InspectorState
            {
                autoRefresh = _autoRefresh,
            };
        }

        private void FromSavedState(InspectorState st)
        {
            _autoRefresh = st.autoRefresh;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            {
                _autoRefresh = EditorGUILayout.Toggle(
                    new GUIContent("Auto refresh",
                        "Refreshes the list automatically. (Warning: can impact performance.)"), _autoRefresh
                );
                EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying || _autoRefresh);
                {
                    if (GUILayout.Button("Refresh"))
                    {
                        if (_tableView == null) return;

                        _tableView.GetData(PoolInfoGatherer.GetInstances());
                        Repaint();
                    }
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(new GUIContent("Inspector only works when in Play Mode."));
                return;
            }

            if (_tableView == null) return;
            var tableViewRect = EditorGUILayout.GetControlRect(
                false,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );
            _tableView.OnGUI(tableViewRect);

            EditorPrefs.SetString(
                EditorPrefKey,
                JsonUtility.ToJson(
                    ToSavedState()
                )
            );
        }

        private void Update()
        {
            if (!EditorApplication.isPlaying || !_autoRefresh) return;
            if (_tableView == null) return;

            _tableView.GetData(PoolInfoGatherer.GetInstances());
            Repaint();
        }

        [Serializable]
        internal class InspectorState
        {
            public bool autoRefresh;
        }
    }

    internal class PoolTableView : TreeView
    {
        private List<PooledObjectsBase> _data = new List<PooledObjectsBase>();

        public PoolTableView(TreeViewState state) : base(state, CreateHeader())
        {
            Reload();
        }

        private static MultiColumnHeader CreateHeader()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Pool name"),
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Instance Usage"),
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("On exhaustion"),
                },
            };
            var header = new MultiColumnHeader(new MultiColumnHeaderState(columns));
            header.ResizeToFit();
            return header;
        }

        public void GetData(List<PooledObjectsBase> data)
        {
            if (_data.Count == data.Count)
            {
                Reload();
                return;
            }

            _data = data;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var id = 0;
            var root = new TreeViewItem { id = ++id, depth = -1, displayName = "Root" };
            var items = new List<TreeViewItem>();
            _data.Select(p => new PoolTreeViewItem
            {
                id = ++id,
                depth = 0,
                name = p.GetType().FullName,
                instanceCount = p.instanceCount,
                freeInstanceCount = p.AvailableInstances,
                behaviour = p.ExhaustionBehaviour,
            }).ToList().ForEach(i => items.Add(i));
            SetupParentsAndChildrenFromDepths(root, items);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            switch (args.item)
            {
                case PoolTreeViewItem item:
                {
                    for (var i = 0; i < args.GetNumVisibleColumns(); i++)
                    {
                        var rect = args.GetCellRect(i);
                        var columnIndex = args.GetColumn(i);

                        // Intentionally using columnIndex here,
                        // because columns can be hidden.
                        switch (columnIndex)
                        {
                            case 0:
                                rect.xMin += GetContentIndent(item);
                                EditorGUI.LabelField(rect, item.name);
                                break;
                            case 1:
                                EditorGUI.ProgressBar(rect,
                                    (float)item.freeInstanceCount / item.instanceCount,
                                    $"{item.freeInstanceCount} / {item.instanceCount}"
                                );
                                break;
                            case 2:
                                EditorGUI.LabelField(rect, item.behaviour.ToLabelFieldText());
                                break;
                        }
                    }

                    break;
                }
                default:
                    base.RowGUI(args);
                    break;
            }
        }

        [Serializable]
        public class PoolTreeViewItem : TreeViewItem
        {
            public string name;
            public int instanceCount;
            public int freeInstanceCount;
            public ExhaustionBehaviour behaviour;
        }
    }

    public static class ExhaustionBehaviourExtension
    {
        public static string ToLabelFieldText(this ExhaustionBehaviour eb)
        {
            return eb switch
            {
                ExhaustionBehaviour.Throw => "Throw exception",
                ExhaustionBehaviour.NullOrDefault => "Return null/default",
                ExhaustionBehaviour.AddOne => "Add new one",
                ExhaustionBehaviour.Double => "Double the instances",
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }
}