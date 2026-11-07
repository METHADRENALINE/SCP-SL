namespace SecretAPI.Extensions.AdminToys;

using LabApi.Features.Wrappers;
using UnityEngine;

public static partial class AdminToyExtensions
{
    extension(TextToy toy)
    {
        public TextToy WithText(string text)
        {
            toy.TextFormat = text;
            return toy;
        }

        public TextToy WithSize(Vector2 size)
        {
            toy.DisplaySize = size;
            return toy;
        }
    }
}
