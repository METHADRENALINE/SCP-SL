namespace SecretAPI.Extensions.AdminToys;

using System;
using global::AdminToys;
using LabApi.Features.Wrappers;

public static partial class AdminToyExtensions
{
    extension(InteractableToy toy)
    {
        public InteractableToy WhenInteracted(Action<Player> action)
        {
            toy.OnInteracted += action;
            return toy;
        }

        public InteractableToy WhenSearching(Action<Player> action)
        {
            toy.OnSearching += action;
            return toy;
        }

        public InteractableToy WhenSearched(Action<Player> action)
        {
            toy.OnSearched += action;
            return toy;
        }

        public InteractableToy WhenSearchAborted(Action<Player> action)
        {
            toy.OnSearchAborted += action;
            return toy;
        }

        public InteractableToy WithShape(InvisibleInteractableToy.ColliderShape shape)
        {
            toy.Shape = shape;
            return toy;
        }

        public InteractableToy WithInteractionDuration(float duration)
        {
            toy.InteractionDuration = duration;
            return toy;
        }

        public InteractableToy IsLocked(bool locked)
        {
            toy.IsLocked = locked;
            return toy;
        }
    }
}
