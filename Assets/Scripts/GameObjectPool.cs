using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class GameObjectPool
{
    private readonly Stack<GameObject> freeObjects = new();
    private readonly HashSet<GameObject> borrowedObjects = new();
    private readonly Func<GameObject> createFunc;
    private readonly Transform parent;

    public GameObjectPool(Func<GameObject> createFunc, int preloadCount, Transform parent)
    {
        this.createFunc = createFunc;
        this.parent = parent;

        for (var i = 0; i < preloadCount; i++)
        {
            var obj = CreatePooledObject();
            obj.SetActive(false);
            freeObjects.Push(obj);
        }
    }

    public GameObject Rent()
    {
        GameObject obj;
        if (freeObjects.Count > 0)
        {
            obj = freeObjects.Pop();
        }
        else
        {
            obj = CreatePooledObject();
        }

        obj.SetActive(true);
        borrowedObjects.Add(obj);
        return obj;
    }

    public void Return(GameObject obj)
    {
        if (obj == null || !borrowedObjects.Remove(obj))
        {
            return;
        }

        obj.SetActive(false);
        obj.transform.SetParent(parent, false);
        freeObjects.Push(obj);
    }

    public void ReturnAll()
    {
        var snapshot = new List<GameObject>(borrowedObjects);
        for (var i = 0; i < snapshot.Count; i++)
        {
            Return(snapshot[i]);
        }
    }

    private GameObject CreatePooledObject()
    {
        var obj = createFunc();
        obj.transform.SetParent(parent, false);
        return obj;
    }
}
