using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PeakCoop.Local
{
    /// <summary>
    /// A cópia de uma vaga de algo que o jogo tem um só (a câmera, o HUD): o objeto é copiado desligado e acorda no
    /// contexto da vaga. Os campos estáticos de instância que ele preenche ao acordar (Singleton&lt;T&gt;._instance,
    /// "public static T instance") passam a ser da vaga: valem só enquanto a vaga está no contexto.
    /// </summary>
    static class SeatClones
    {
        public static GameObject Clone(ExtraSeat s, GameObject source, string name)
        {
            // a cópia nasce desligada (dentro de um pai desligado), para acordar só no contexto da vaga
            var holder = new GameObject("PeakCoop holder");
            holder.SetActive(false);
            GameObject copy = UnityEngine.Object.Instantiate(source, holder.transform);
            copy.name = name;
            FieldInfo[] statics = InstanceFields(copy);

            SeatContext.Run(s.Seat, () =>
            {
                // sem isto, o Awake de cada Singleton<T> da cópia acharia a instância do jogo e destruiria a cópia
                var saved = new object[statics.Length];
                for (int i = 0; i < statics.Length; i++) { saved[i] = statics[i].GetValue(null); statics[i].SetValue(null, null); }
                try
                {
                    copy.transform.SetParent(source.transform.parent, true);
                    UnityEngine.Object.Destroy(holder);
                }
                finally
                {
                    for (int i = 0; i < statics.Length; i++)
                    {
                        object mine = statics[i].GetValue(null);
                        statics[i].SetValue(null, saved[i]);
                        if (mine != null)
                        {
                            s.Singletons[statics[i]] = mine;
                            SeatContext.TrackSingleton(statics[i]);
                        }
                    }
                }
            });
            SeatComponents.Register(copy, s.Seat);
            return copy;
        }

        /// <summary>Os campos estáticos "a instância do jogo" dos tipos que o objeto traz.</summary>
        static FieldInfo[] InstanceFields(GameObject go)
        {
            var list = new List<FieldInfo>();
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                for (Type t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Zorro.Core.Singleton<>))
                        Add(list, t.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic));
                    foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (f.FieldType == t && !f.IsInitOnly && (f.Name == "instance" || f.Name == "Instance" || f.Name == "_instance"))
                            Add(list, f);
                }
            }
            return list.ToArray();
        }

        static void Add(List<FieldInfo> list, FieldInfo f)
        {
            if (f != null && !list.Contains(f)) list.Add(f);
        }
    }
}
