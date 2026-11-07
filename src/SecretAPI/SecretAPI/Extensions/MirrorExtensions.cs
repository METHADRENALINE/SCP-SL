namespace SecretAPI.Extensions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using global::AdminToys;
using LabApi.Features.Wrappers;
using Mirror;
using Logger = LabApi.Features.Console.Logger;

#pragma warning disable SA1117 // The parameters should all be placed on the same line or each parameter should be placed on its own line

public static class MirrorExtensions
{
    private static readonly Dictionary<Type, Delegate> TypeToWriterCache = new();
    private static readonly ExtraWriteInfo[] SubWriteInfos = new[]
    {
        new ExtraWriteInfo(typeof(AdminToyBase), 16UL, 1),
    };

    public static void SendFakeRpcMessage(this Player target, NetworkBehaviour behaviour, Type type, string rpcName, params object[] values)
    {
        if (!target.GameObject)
            return;

        NetworkWriterPooled pooledWriter = NetworkWriterPool.Get();

        foreach (object obj in values)
            ProperWrite(pooledWriter, obj);

        RpcMessage rpcMessage = new()
        {
            netId = behaviour.netId,
            componentIndex = behaviour.ComponentIndex,
            functionHash = (ushort)ReflectionExtensions.GetLongFuncName(type, rpcName).GetStableHashCode(),
            payload = pooledWriter.ToArraySegment(),
        };

        target.Connection.Send(rpcMessage);
        NetworkWriterPool.Return(pooledWriter);
    }

    public static void SendFakeSyncListData<T>(this Player target, NetworkBehaviour behaviour, ulong listIndex, SyncListChange<T> change)
    {
        if (listIndex <= 0)
        {
            Logger.Warn($"[MirrorExtensions.SendFakeSyncListData] Index is {listIndex} - Expected 1 or higher!: + {new StackTrace()}");
            return;
        }

        SendFakeState(target, behaviour, writer =>
        {
            writer.WriteULong(listIndex);

            writer.WriteUInt(1);
            writer.WriteByte((byte)change.Operation);
            switch (change.Operation)
            {
                case SyncList<T>.Operation.OP_ADD:
                    writer.Write(change.Item);
                    break;
                case SyncList<T>.Operation.OP_INSERT:
                case SyncList<T>.Operation.OP_SET:
                    writer.WriteUInt((uint)change.Index);
                    writer.Write(change.Item);
                    break;
                case SyncList<T>.Operation.OP_REMOVEAT:
                    writer.WriteUInt((uint)change.Index);
                    break;
            }
        }, null);
    }

    public static void SendFakeSyncVar<T>(this Player target, NetworkBehaviour behaviour, ulong dirtyBit, T value)
    {
        SendFakeState(target, behaviour, null, writer =>
        {
            writer.WriteULong(dirtyBit);

            ExtraWriteInfo info = GetExtraWriteInfo(behaviour.GetType());
            bool isWritten = false;

            if (dirtyBit > info.MaxDirtyBit)
            {
                WriteExtraDirtyBit(info, writer, dirtyBit);
                isWritten = true;
            }

            writer.Write(value);

            if (!isWritten)
                WriteExtraDirtyBit(info, writer, dirtyBit);
        });
    }

    public static void SendFakeState(this Player target, NetworkBehaviour behaviour, Action<NetworkWriter>? syncObjectWriter, Action<NetworkWriter>? syncVarWriter)
    {
        if (!target.GameObject)
            return;

        using NetworkWriterPooled pooledWriter = NetworkWriterPool.Get();

        int index = behaviour.netIdentity.NetworkBehaviours.IndexOf(behaviour);
        ulong mask = (ulong)(1 << index);
        Compression.CompressVarUInt(pooledWriter, mask);

        int headerPosition = pooledWriter.Position;
        pooledWriter.WriteByte(0);
        int contentPosition = pooledWriter.Position;

        if (syncObjectWriter != null)
            syncObjectWriter.Invoke(pooledWriter);
        else
            pooledWriter.WriteULong(0);

        if (syncVarWriter != null)
        {
            syncVarWriter.Invoke(pooledWriter);
        }
        else
        {
            pooledWriter.WriteULong(0);
            WriteExtraDirtyBit(GetExtraWriteInfo(behaviour.GetType()), pooledWriter);
        }

        int endPosition = pooledWriter.Position;
        pooledWriter.Position = headerPosition;
        int size = endPosition - contentPosition;
        byte safety = (byte)(size & 0xFF);
        pooledWriter.WriteByte(safety);
        pooledWriter.Position = endPosition;

        target.Connection.Send(new EntityStateMessage()
        {
            netId = behaviour.netId,
            payload = pooledWriter.ToArraySegment(),
        });
    }

    public static void ProperWrite(this NetworkWriter writer, object obj)
    {
        Type type = obj.GetType();

        if (TypeToWriterCache.TryGetValue(type, out Delegate del))
        {
            del.DynamicInvoke(writer, obj);
            return;
        }

        Type genericType = typeof(Writer<>).MakeGenericType(obj.GetType());
        FieldInfo? writeField = genericType.GetField("write", BindingFlags.Static | BindingFlags.Public);
        if (writeField == null)
        {
            Logger.Warn($"Tried to write type: {obj.GetType()} but has no NetworkWriter!");
            return;
        }

        object? writeDelegate = writeField.GetValue(null);
        if (writeDelegate is not Delegate dele)
        {
            Logger.Warn($"Writer<{obj.GetType()}>.write is not a delegate!");
            return;
        }

        TypeToWriterCache.Add(type, dele);
        dele.DynamicInvoke(writer, obj);
    }

    private static void WriteExtraDirtyBit(ExtraWriteInfo writeInfo, NetworkWriter writer, ulong dirtyBit = 0)
    {
        if (!writeInfo.IsSet)
            return;

        for (int index = 0; index < writeInfo.ExtraWriteCount; ++index)
            writer.WriteULong(dirtyBit);
    }

    private static ExtraWriteInfo GetExtraWriteInfo(Type type)
    {
        foreach (ExtraWriteInfo subWriteInfo in SubWriteInfos)
        {
            if (type.IsSubclassOf(subWriteInfo.ClassType))
                return subWriteInfo;
        }

        return default;
    }

    public struct SyncListChange<T>
    {
        public required SyncList<T>.Operation Operation;

        public int Index;

        public T Item;
    }

    private readonly struct ExtraWriteInfo(Type classType, ulong dirtyBit, int writeCount)
    {
        public readonly Type ClassType = classType;
        public readonly ulong MaxDirtyBit = dirtyBit;
        public readonly int ExtraWriteCount = writeCount;
        public readonly bool IsSet = classType != null;
    }
}
