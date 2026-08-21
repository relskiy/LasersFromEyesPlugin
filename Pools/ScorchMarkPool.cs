using System.Collections.Generic;
using AdminToys;
using EyeLasers.Controllers;
using Mirror;
using UnityEngine;

namespace EyeLasers.Pools
{
    public class ScorchMarkPool
    {
        private readonly Queue<PrimitiveObjectToy> _activeMarks = new Queue<PrimitiveObjectToy>(32);
        private readonly List<PrimitiveObjectToy> _pool = new List<PrimitiveObjectToy>(32);
        private readonly int _maxSize;

        public ScorchMarkPool(int maxSize)
        {
            _maxSize = maxSize;
        }

        public void Spawn(Vector3 point, Vector3 normal)
        {
            PrimitiveObjectToy mark = null;

            if (_pool.Count > 0)
            {
                int last = _pool.Count - 1;
                mark = _pool[last];
                _pool.RemoveAt(last);
            }

            if (mark == null)
            {
                if (_activeMarks.Count >= _maxSize)
                {
                    mark = _activeMarks.Dequeue();
                }
                else
                {
                    mark = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, LaserController.ColorScorch);
                    if (mark == null) return;
                }
            }

            if (mark != null)
            {
                mark.transform.position = point + (normal * 0.005f);
                mark.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
                mark.transform.localScale = LaserController.ScaleScorch;
                _activeMarks.Enqueue(mark);
            }
        }

        public void Clear()
        {
            while (_activeMarks.Count > 0)
            {
                var obj = _activeMarks.Dequeue();
                if (obj != null)
                {
                    try { NetworkServer.Destroy(obj.gameObject); } catch { }
                }
            }

            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                {
                    try { NetworkServer.Destroy(_pool[i].gameObject); } catch { }
                }
            }
            _pool.Clear();
        }
    }
}