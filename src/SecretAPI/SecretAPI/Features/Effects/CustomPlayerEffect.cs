namespace SecretAPI.Features.Effects;

using System;
using System.Collections.Generic;
using CustomPlayerEffects;
using LabApi.Features.Wrappers;
using SecretAPI.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

using Logger = LabApi.Features.Console.Logger;


public abstract class CustomPlayerEffect : StatusEffectBase
{
    [Obsolete("This is becoming readonly in 4.0 - Use Register<T>()")]
    public static List<Type> EffectsToRegister { get; } = [];

    public Player Owner => field ??= Player.Get(Hub);

    public static void Register<T>()
        where T : StatusEffectBase => Register(typeof(T));

    public static void Register(Type type)
    {
        if (!type.IsAssignableFrom(typeof(StatusEffectBase)))
        {
            Logger.Error($"[CustomPlayerEffect.Register] {type.FullName} does not inherit StatusEffectBase!");
            return;
        }

        if (type.IsAbstract)
        {
            Logger.Error($"[CustomPlayerEffect.Register] {type.FullName} is abstract!");
            return;
        }

#pragma warning disable CS0618 // Type or member is obsolete
        EffectsToRegister.Add(type);
    }

    public override string ToString() => $"{GetType().Name}: Owner ({Owner}) - Intensity ({Intensity}) - Duration {Duration}";

    [CallOnLoad]
    internal static void Initialize()
    {
        EffectsToRegister.Add(typeof(Energized));
        EffectsToRegister.Add(typeof(Depleted));
        EffectsToRegister.Add(typeof(BlastResistance));

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        Transform playerEffects = PrefabStore<ReferenceHub>.Prefab.playerEffectsController.effectsGameObject.transform;
        foreach (Type type in EffectsToRegister)
        {
            if (!typeof(StatusEffectBase).IsAssignableFrom(type))
            {
                Logger.Error($"[CustomPlayerEffect.OnSceneLoaded] {type.FullName} is not a valid StatusEffectBase and thus could not be registered!");
                continue;
            }

            new GameObject(type.Name, type).transform.parent = playerEffects;
        }
    }
}
