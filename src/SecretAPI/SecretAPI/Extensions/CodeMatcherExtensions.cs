namespace SecretAPI.Extensions;

using System;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

public static class CodeMatcherExtensions
{
    extension(CodeMatcher matcher)
    {
        public CodeMatcher EndAndBacktrack(int backtrackCount) => matcher.End().Advance(-backtrackCount);

        public CodeMatcher DeclareLocal(Type localType, out LocalBuilder localBuilder)
        {
            localBuilder = matcher.generator.DeclareLocal(localType);
            return matcher;
        }

        public CodeMatcher DeclareLocal(Type localType, out int localIndex)
        {
            DeclareLocal(matcher, localType, out LocalBuilder builder);
            localIndex = builder.LocalIndex;
            return matcher;
        }

        public CodeMatcher GetFirstLabel(out Label label)
        {
            label = matcher.Labels.FirstOrDefault();
            return matcher;
        }

        public CodeMatcher GetFirstLabelAt(int position, out Label label)
        {
            label = matcher.codes[position].labels.FirstOrDefault();
            return matcher;
        }
    }
}
