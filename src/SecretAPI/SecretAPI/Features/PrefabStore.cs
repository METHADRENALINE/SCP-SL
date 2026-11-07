namespace SecretAPI.Features;

using System.Collections.Generic;
using System.Linq;
using AdminToys;
using Interactables.Interobjects;
using Mirror;
using UnityEngine;

public static class PrefabStore<TPrefab>
    where TPrefab : NetworkBehaviour
{
    public static TPrefab Prefab
    {
        get
        {
            if (field)
                return field;

            if (typeof(TPrefab) == typeof(ReferenceHub))
                return field = NetworkManager.singleton.playerPrefab.GetComponent<TPrefab>();

            return field = AllComponentPrefabs.FirstOrDefault()!;
        }
    }

    public static TPrefab[] AllComponentPrefabs
    {
        get
        {
            if (field != null)
                return field;

            List<TPrefab> allPrefabs = new();
            foreach (GameObject gameObject in NetworkClient.prefabs.Values)
            {
                if (gameObject.TryGetComponent(out TPrefab prefab))
                    allPrefabs.Add(prefab);
            }

            return field = allPrefabs.ToArray();
        }
    }
}
