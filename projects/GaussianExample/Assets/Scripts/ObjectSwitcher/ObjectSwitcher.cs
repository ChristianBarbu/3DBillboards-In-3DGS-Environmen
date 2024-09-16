using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ObjectSwitcher
{
    /// <summary>
    /// Small script for more easily enabling and disabling objects for demonstration purposes.
    /// This can also be done by adding a parent object, which then adds all the children to the list.
    /// </summary>
    public class ObjectSwitcher : MonoBehaviour
    {
        public List<GameObject> switchObjectList;
        public int pointer;

        public List<GameObject> addByParentList;
        
        public void MovePointerToNextSwitchObject()
        {
            MovePointer(1);
        }

        public void MovePointerToPreviousSwitchObject()
        {
            MovePointer(-1);
        }
        
        public void MovePointer(int direction)
        {
            if (switchObjectList.Count == 0) return;

            switchObjectList[pointer].SetActive(false);
            pointer = (pointer + direction + switchObjectList.Count) % switchObjectList.Count;
            switchObjectList[pointer].SetActive(true);
        }

        public void ResetPointer()
        {
            if (switchObjectList.Count == 0) return;
            
            switchObjectList[pointer].SetActive(false);
            pointer = 0;
            switchObjectList[pointer].SetActive(true);
        }

        public void ClearList()
        {
            switchObjectList.Clear();
        }

        public void AddToListByParent()
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
        }

        public void AddByParentListToObjectSwitchList()
        {
            AddToListByParent();
            addByParentList.Clear();
        }
        
    }
}
