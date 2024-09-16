using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ObjectSwitcher.Editor
{
    public class ObjectSwitcherWindow : EditorWindow
    {
        private List<GameObject> switchObjectList = new List<GameObject>();
        private List<GameObject> addByParentList = new List<GameObject>();
        private int pointer;
        private Vector2 scrollPosition;
        private bool showSwitchObjectList = true;
        private bool showAddByParentList = true;

        [MenuItem("Tools/Object Switcher")]
        public static void ShowWindow()
        {
            GetWindow<ObjectSwitcherWindow>("Object Switcher");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Object Switcher", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            // Switch Object List
            showSwitchObjectList = EditorGUILayout.Foldout(showSwitchObjectList, "Switch Object List", true);
            if (showSwitchObjectList)
            {
                for (int i = 0; i < switchObjectList.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    switchObjectList[i] = (GameObject)EditorGUILayout.ObjectField(switchObjectList[i], typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        switchObjectList.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Add Object"))
                {
                    switchObjectList.Add(null);
                }
            }

            EditorGUILayout.Space();

            // Add By Parent List
            showAddByParentList = EditorGUILayout.Foldout(showAddByParentList, "Add By Parent List", true);
            if (showAddByParentList)
            {
                for (int i = 0; i < addByParentList.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    addByParentList[i] = (GameObject)EditorGUILayout.ObjectField(addByParentList[i], typeof(GameObject), true);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    {
                        addByParentList.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Add Parent"))
                {
                    addByParentList.Add(null);
                }
            }

            EditorGUILayout.Space();

            // Buttons
            if (GUILayout.Button("Add by parent"))
            {
                AddByParentListToObjectSwitchList();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("<"))
            {
                MovePointer(-1);
            }
            if (GUILayout.Button(">"))
            {
                MovePointer(1);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Reset Switch"))
            {
                ResetPointer();
            }

            if (GUILayout.Button("Clear List"))
            {
                ClearList();
            }

            EditorGUILayout.EndScrollView();
        }

        private void MovePointer(int direction)
        {
            if (switchObjectList.Count == 0) return;

            if (switchObjectList[pointer] != null)
                switchObjectList[pointer].SetActive(false);
            
            pointer = (pointer + direction + switchObjectList.Count) % switchObjectList.Count;
            
            if (switchObjectList[pointer] != null)
                switchObjectList[pointer].SetActive(true);
        }

        private void ResetPointer()
        {
            if (switchObjectList.Count == 0) return;

            foreach (var switchObject in switchObjectList)
            {
                if(switchObject != null)
                    switchObject.SetActive(false);
                pointer = 0;
            }
        }

        private void ClearList()
        {
            switchObjectList.Clear();
        }

        private void AddByParentListToObjectSwitchList()
        {
            foreach (var parent in addByParentList)
            {
                if (parent != null)
                {
                    for (int i = 0; i < parent.transform.childCount; i++)
                    {
                        switchObjectList.Add(parent.transform.GetChild(i).gameObject);
                    }
                }
            }
            addByParentList.Clear();
        }
    }
}