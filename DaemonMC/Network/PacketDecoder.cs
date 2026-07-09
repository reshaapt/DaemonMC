using System.Net;
using System.Numerics;
using System.Text;
using DaemonMC.Items;
using DaemonMC.Network.Enumerations;
using DaemonMC.Network.RakNet;
using DaemonMC.Telemetry;
using DaemonMC.Utils;
using DaemonMC.Utils.Game;
using DaemonMC.Utils.Text;
using System.Runtime.CompilerServices;

namespace DaemonMC.Network
{
    public class PacketDecoder
    {
        public readonly object Sync = new object();
        public List<byte[]> packetBuffers = new List<byte[]>();
        public byte[] buffer;
        public int readOffset;
        public IPEndPoint clientEp;
        public int protocolVersion = 0;
        public Player player;
        public List<PacketTraceOperation> TraceOperations { get; } = new();

        public PacketDecoder(byte[] byteBuffer, IPEndPoint ep)
        {
            buffer = byteBuffer;
            readOffset = 0;
            clientEp = ep;
            protocolVersion = RakSessionManager.getSession(ep).protocolVersion;
        }

        public void RakDecoder(PacketDecoder decoder, int recv)
        {
            Server.DatGrIn++;

            var pkid = decoder.ReadByte();
            if (pkid <= 127 || pkid >= 141) { Log.packetIn(decoder.clientEp, (Info.RakNet)pkid); }

            switch ((Info.RakNet)pkid)
            {
                case Info.RakNet.UnconnectedPing:
                    new UnconnectedPing().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.UnconnectedPong:
                    new UnconnectedPong().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.OpenConnectionRequest1:
                    new OpenConnectionRequest1().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.OpenConnectionReply1:
                    new OpenConnectionReply1().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.OpenConnectionRequest2:
                    new OpenConnectionRequest2().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.OpenConnectionReply2:
                    new OpenConnectionReply2().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.ACK:
                    new ACK().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                case Info.RakNet.NACK:
                    new NACK().DecodePacket(decoder, PacketHandler.Raknet);
                    break;
                default:
                    if (pkid >= 128 && pkid <= 141)
                    {
                        Reliability.ReliabilityHandler(decoder, recv);
                    }
                    else
                    {
                        Log.error($"[Server] Unknown RakNet packet: {pkid}");
                        ToDataTypes.HexDump(decoder.buffer, recv);
                    }
                    break;
            }

            if (decoder.readOffset < recv)
            {
                Log.warn($"{recv - decoder.readOffset} bytes left while reading {(Info.RakNet)pkid}");
            }
            packetHandler(decoder);
            PacketDecoderPool.Return(decoder);
        }

        public void packetHandler(PacketDecoder decoderT)
        {
            foreach (byte[] buffer in packetBuffers)
            {
                PacketDecoder decoder = PacketDecoderPool.Get(buffer, decoderT.clientEp);
                var pkid = decoder.ReadByte();
                Log.packetIn(decoder.clientEp, (Info.RakNet)pkid);

                switch ((Info.RakNet)pkid)
                {
                    case Info.RakNet.ConnectionRequest:
                        new ConnectionRequest().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.ConnectionRequestAccepted:
                        new ConnectionRequestAccepted().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.NewIncomingConnection:
                        new NewIncomingConnection().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.ConnectedPing:
                        new ConnectedPing().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.ConnectedPong:
                        new ConnectedPong().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.Disconnect:
                        new RakDisconnect().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    case Info.RakNet.GamePacket:
                        new GamePacket().DecodePacket(decoder, PacketHandler.Raknet);
                        break;
                    default:
                        Log.error($"[Server] Unknown RakNet packet: {pkid}");
                        break;
                }
                if (decoder.readOffset < decoder.buffer.Length && pkid != 254)
                {
                    Log.warn($"{decoder.buffer.Length - decoder.readOffset} bytes left while reading {(Info.RakNet)pkid}");
                }
                PacketDecoderPool.Return(decoder);
            }
        }

        public void Reset(byte[] buffer)
        {
            this.buffer = buffer;
            this.readOffset = 0;
            this.packetBuffers.Clear();
            this.TraceOperations.Clear();
        }

        public void BeginTrace()
        {
            TraceOperations.Clear();
        }

        private void TraceRead(string operation, int start, string property = "")
        {
            int length = readOffset - start;
            if (length <= 0)
                return;

            TraceOperations.Add(new PacketTraceOperation
            {
                Operation = operation,
                Property = property,
                Offset = start,
                Length = length
            });
        }

        public bool ReadBool([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            byte b = buffer[readOffset];
            readOffset += 1;
            TraceRead(nameof(ReadBool), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return b == 1 ? true : false;
        }

        public int ReadInt([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            int a = BitConverter.ToInt32(buffer, readOffset);
            readOffset += 4;
            TraceRead(nameof(ReadInt), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return a;
        }

        public float ReadFloat([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            float a = BitConverter.ToSingle(buffer, readOffset);
            readOffset += 4;
            TraceRead(nameof(ReadFloat), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return a;
        }

        public int ReadIntBE([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            Array.Reverse(buffer, readOffset, 4);
            int a = BitConverter.ToInt32(buffer, readOffset);
            readOffset += 4;
            TraceRead(nameof(ReadIntBE), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return a;
        }

        public int ReadVarInt([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            int value = 0;
            int size = 0;

            while (true)
            {
                byte currentByte = buffer[readOffset++];
                value |= (currentByte & 0x7F) << (size * 7);

                if ((currentByte & 0x80) == 0)
                {
                    break;
                }

                size++;
            }

            TraceRead(nameof(ReadVarInt), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public int ReadSignedVarInt([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {

            int start = readOffset;
            int rawVarInt = ReadVarInt();
            int value = (rawVarInt >> 1) ^ -(rawVarInt & 1);
            TraceRead(nameof(ReadSignedVarInt), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public short ReadSignedShort([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            short value = (short)((buffer[readOffset] << 8) | buffer[readOffset + 1]);
            readOffset += 2;
            TraceRead(nameof(ReadSignedShort), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public short ReadShortBE([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            short value = (short)(buffer[readOffset + 1] | (buffer[readOffset] << 8));
            readOffset += 2;
            TraceRead(nameof(ReadShortBE), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public ushort ReadShort([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            ushort value = (ushort)((buffer[readOffset] << 8) | buffer[readOffset + 1]);
            readOffset += 2;
            TraceRead(nameof(ReadShort), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return (ushort)((value >> 8) | (value << 8));
        }

        public byte ReadByte([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            byte b = buffer[readOffset];
            readOffset += 1;
            TraceRead(nameof(ReadByte), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return b;
        }

        public void ReadBytes(byte[] data, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            Array.Copy(buffer, readOffset, data, 0, data.Length);
            readOffset += data.Length;
            TraceRead(nameof(ReadBytes), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
        }

        public byte[] ReadBytes(int count, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            byte[] result = new byte[count];
            Array.Copy(buffer, readOffset, result, 0, count);
            readOffset += count;
            TraceRead(nameof(ReadBytes), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));

            return result;
        }

        public byte[] ReadBytes([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            int length = ReadVarInt();
            byte[] result = new byte[length];
            Array.Copy(buffer, readOffset, result, 0, length);
            readOffset += length;
            TraceRead(nameof(ReadBytes), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));

            return result;
        }

        public long ReadLong([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            long value = BitConverter.ToInt64(buffer, readOffset);
            readOffset += 8;
            TraceRead(nameof(ReadLong), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public long ReadLongLE([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            Array.Reverse(buffer, readOffset, 8);
            long value = BitConverter.ToInt64(buffer, readOffset);
            readOffset += 8;
            TraceRead(nameof(ReadLongLE), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public string ReadMagic([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 16; ++i)
            {
                sb.Append(buffer[readOffset + i].ToString("X2"));
            }
            readOffset += 16;
            TraceRead(nameof(ReadMagic), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return sb.ToString();
        }

        public string ReadRakString([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            short length = ReadShortBE();
            if (length < 0 || readOffset + length > buffer.Length)
            {
                throw new Exception($"Invalid rakstring lenght {length}");
            }
            string str = Encoding.UTF8.GetString(buffer, readOffset, length);
            readOffset += length;

            TraceRead(nameof(ReadRakString), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return str;
        }

        public string ReadString([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            int length = ReadVarInt();
            if (length < 0 || readOffset + length > buffer.Length)
            {
                throw new Exception($"Invalid string lenght {length}");
            }
            string str = Encoding.UTF8.GetString(buffer, readOffset, length);
            readOffset += length;

            TraceRead(nameof(ReadString), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return str;
        }

        public List<string> ReadStringList()
        {
            List<string> list = new List<string>();
            int count = ReadVarInt();
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadString());
            }
            return list;
        }

        public short ReadMTU(int lenght, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            int paddingSize = lenght - readOffset;

            short estimatedMTU = (short)(readOffset + paddingSize + 28);

            readOffset = (paddingSize + readOffset);

            TraceRead(nameof(ReadMTU), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return estimatedMTU;
        }

        public IPAddressInfo ReadAddress([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            byte ipVersion = buffer[readOffset];
            readOffset++;

            IPAddressInfo ipAddressInfo = new IPAddressInfo();

            if (ipVersion == 4)
            {
                ipAddressInfo.IPAddress = new byte[4];
                Array.Copy(buffer, readOffset, ipAddressInfo.IPAddress, 0, 4);
                readOffset += 4;
                ipAddressInfo.Port = ReadShort();
            }
            else if (ipVersion == 6)
            {
                ReadShort(); //address family
                ipAddressInfo.Port = ReadShort();
                ReadInt(); //idk
                ipAddressInfo.IPAddress = new byte[16];
                Array.Copy(buffer, readOffset, ipAddressInfo.IPAddress, 0, 16);
                readOffset += 16;
                ReadInt(); //also idk
            }

            TraceRead(nameof(ReadAddress), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return ipAddressInfo;
        }

        public IPAddressInfo[] ReadInternalAddress()
        {
            List<IPAddressInfo> internalAddresses = new List<IPAddressInfo>();

            for (int i = 0; i < 20; i++)
            {
                byte ipVersion = buffer[readOffset];
                IPAddressInfo ipAddressInfo = new IPAddressInfo();

                if ((ipVersion == 4 && buffer.Length - readOffset > 16 + 6) || (ipVersion == 6 && buffer.Length - readOffset > 16 + 28))
                {
                    internalAddresses.Add(ReadAddress());
                }
                else
                {
                    Log.warn($"Unknown IP version {ipVersion}");
                    break;
                }
            }

            return internalAddresses.ToArray();
        }

        public uint ReadUInt24LE([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            uint uint24leValue = (uint)(buffer[readOffset] | (buffer[readOffset + 1] << 8) | (buffer[readOffset + 2] << 16));
            readOffset += 3;
            TraceRead(nameof(ReadUInt24LE), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return uint24leValue;
        }

        public long ReadVarLong([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            int start = readOffset;
            long value = 0;
            int size = 0;

            while (true)
            {
                byte currentByte = buffer[readOffset++];
                value |= (long)(currentByte & 0x7F) << (size * 7);

                if ((currentByte & 0x80) == 0)
                {
                    break;
                }

                size++;
            }

            TraceRead(nameof(ReadVarLong), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public long ReadSignedVarLong([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {

            int start = readOffset;
            long rawVarLong = ReadVarLong();
            long value = (rawVarLong >> 1) ^ -(rawVarLong & 1);
            TraceRead(nameof(ReadSignedVarLong), start, PacketTracePropertyResolver.FromSourceLine(callerFilePath, callerLineNumber));
            return value;
        }

        public Guid ReadUUID()
        {
            byte[] mostSignificantBits = new byte[8];
            byte[] leastSignificantBits = new byte[8];

            ReadBytes(mostSignificantBits);
            ReadBytes(leastSignificantBits);

            mostSignificantBits = mostSignificantBits.Reverse().ToArray();
            leastSignificantBits = leastSignificantBits.Reverse().ToArray();

            byte[] uuidBytes = mostSignificantBits.Concat(leastSignificantBits).ToArray();
            return new Guid(uuidBytes);
        }

        public Vector3 ReadVec3()
        {
            var value = new Vector3()
            {
                X = ReadFloat(),
                Y = ReadFloat(),
                Z = ReadFloat()
            };
            return value;
        }

        public Vector3 ReadBlockNetPos()
        {
            var value = new Vector3();
            value.X = ReadSignedVarInt();
            value.Y = ReadSignedVarInt();
            value.Z = ReadSignedVarInt();
            return value;
        }

        public Vector2 ReadVec2()
        {
            var value = new Vector2()
            {
                X = ReadFloat(),
                Y = ReadFloat()
            };
            return value;
        }

        public List<string> ReadPackNames()
        {
            List<string> packs = new List<string>();
            ushort packCount = ReadShort();
            for (int i = 0; i < packCount; i++)
            {
                packs.Add(ReadString());
            }
            return packs;
        }

        public Skin ReadSkin()
        {
            Skin skin = new Skin();

            skin.SkinId = ReadString();
            skin.PlayFabId = ReadString();
            skin.SkinResourcePatch = ReadString();
            skin.SkinImageWidth = ReadInt();
            skin.SkinImageHeight = ReadInt();
            int skinDataLength = ReadVarInt();
            skin.SkinData = ReadBytes(skinDataLength);

            int animatedDataCount = ReadInt();
            skin.AnimatedImageData = new List<AnimatedImageData>();

            for (int i = 0; i < animatedDataCount; i++)
            {
                AnimatedImageData animation = new AnimatedImageData();
                animation.ImageWidth = ReadInt();
                animation.ImageHeight = ReadInt();
                int imageDataLength = ReadVarInt();
                animation.Image = Convert.ToBase64String(ReadBytes(imageDataLength));
                animation.Type = ReadInt();
                animation.Frames = ReadFloat();
                animation.AnimationExpression = ReadInt();

                skin.AnimatedImageData.Add(animation);
            }

            skin.Cape = new Cape();
            skin.Cape.CapeImageWidth = ReadInt();
            skin.Cape.CapeImageHeight = ReadInt();
            int capeDataLength = ReadVarInt();
            skin.Cape.CapeData = ReadBytes(capeDataLength);
            skin.SkinGeometryData = ReadString();
            skin.SkinGeometryDataEngineVersion = ReadString();
            skin.SkinAnimationData = ReadString();
            skin.Cape.CapeId = ReadString();
            ReadString();
            skin.ArmSize = ReadString();
            skin.SkinColor = ReadString();

            int personaPieceCount = ReadInt();
            skin.PersonaPieces = new List<PersonaPiece>();

            for (int i = 0; i < personaPieceCount; i++)
            {
                PersonaPiece part = new PersonaPiece();
                part.PieceId = ReadString();
                part.PieceType = ReadString();
                part.PackId = ReadString();
                part.IsDefault = ReadBool();
                part.ProductId = ReadString();

                skin.PersonaPieces.Add(part);
            }

            int pieceTintCount = ReadInt();
            skin.PieceTintColors = new List<PieceTintColor>();

            for (int i = 0; i < pieceTintCount; i++)
            {
                PieceTintColor part = new PieceTintColor();
                part.PieceType = ReadString();
                int colorCount = ReadInt();
                part.Colors = new List<string>();

                for (int j = 0; j < colorCount; j++)
                {
                    part.Colors.Add(ReadString());
                }

                skin.PieceTintColors.Add(part);
            }

            skin.PremiumSkin = ReadBool();
            skin.PersonaSkin = ReadBool();
            skin.CapeOnClassicSkin = ReadBool();
            ReadBool(); // is primary user
            skin.OverrideSkin = ReadBool();

            return skin;
        }

        public List<Guid> ReadEmotes()
        {
            var EmoteIds = new List<Guid>();
            var size = ReadVarInt();
            for (int v = 0; v < size; v++)
            {
                EmoteIds.Add(ReadUUID());
            }
            return EmoteIds;
        }

        public Item ReadNetItem()
        {
            return ReadItem(true);
        }

        public Item ReadItem(bool network = false)
        {
            var id = 0;
            if (network)
            {
                id = ReadShort();
            }
            else
            {
                id = ReadSignedVarInt();
            }
            if (id != 0 || network)
            {
                Item item = ItemPalette.items.GetValueOrDefault((short)id);

                if (item == null)
                {
                    item = new Items.VanillaItems.Air();
                }
                item.Count = ReadShort();
                item.Aux = ReadVarInt();
                if (ReadBool()) //idk whats this for
                {
                    ReadVarInt(); //variant
                    ReadSignedVarInt(); //stack id
                }
                item.BlockRuntimeId = ReadSignedVarInt();
                ReadString();//nbt data. useless for server auth inventory
                return item;
            }
            return new Items.VanillaItems.Air();
        }

        public List<Item> ReadCraftResultsDeprecatedItems()
        {
            var items = new List<Item>();

            var count = ReadVarInt();
            for (int i = 0; i < count; i++)
            {
                items.Add(ReadItemInstance(true));
            }

            return items;
        }

        public Item ReadItemInstance(bool network = false)
        {
            var id = 0;
            if (network)
            {
                id = ReadShort();
            }
            else
            {
                id = ReadSignedVarInt();
            }
            if (id != 0 || network)
            {
                Item item = ItemPalette.items.GetValueOrDefault((short)id);

                if (item == null)
                {
                    item = new Items.VanillaItems.Air();
                }
                item.Count = ReadShort();
                item.Aux = ReadVarInt();
                item.BlockRuntimeId = ReadSignedVarInt();
                ReadString();//nbt data. useless for server auth inventory
                return item;
            }
            return new Items.VanillaItems.Air();
        }

        public AttributesValues ReadAttributes()
        {
            var values = new AttributesValues();
            values.MovementSpeed = ReadFloat();
            values.UnderwaterMovementSpeed = ReadFloat();
            values.LavaMovementSpeed = ReadFloat();
            values.JumpStrength = ReadFloat();
            values.Health = ReadFloat();
            values.Hunger = ReadFloat();
            if (protocolVersion >= Info.v1_26_20)
            {
                values.FrictionModifier = ReadFloat();
                values.Bounciness = ReadFloat();
                values.AirDragModifier = ReadFloat();
            }
            return values;
        }

        public List<Actions> ReadActions()
        {
            var actions = new List<Actions>();
            var count = ReadVarInt();
            for (int i = 0; i < count; i++)
            {
                var action = new Actions();
                action.ActionsType = (ItemStackRequestActionType)ReadByte();
                action.Amount = ReadByte();
                action.Source = ReadSlotInfo();
                action.Destination = ReadSlotInfo();
            }
            return actions;
        }

        public ItemStackRequestSlotInfo ReadSlotInfo()
        {
            var slotInfo = new ItemStackRequestSlotInfo();
            slotInfo.ContainerName = ReadContainerName();
            slotInfo.Slot = ReadByte();
            slotInfo.NetIdVariant = ReadVarInt();
            return slotInfo;
        }

        public FullContainerName ReadContainerName()
        {
            var containerName = new FullContainerName();
            containerName.ContainerName = (ContainerEnumName)ReadByte();
            containerName.DynamicId = ReadOptional(() => ReadSignedVarInt());
            return containerName;
        }

        public PlayerBlockAction ReadBlockActions()
        {
            var action = new PlayerBlockAction();
            var actionCount = ReadSignedVarInt();
            for (int i = 0; i < actionCount; i++)
            {
                action.ActionType = (PlayerActionType)ReadVarInt();
                switch (action.ActionType)
                {
                    case PlayerActionType.PredictDestroyBlock:
                    case PlayerActionType.StartDestroyBlock:
                    case PlayerActionType.AbortDestroyBlock:
                    case PlayerActionType.CrackBlock:
                    case PlayerActionType.ContinueDestroyBlock:
                        action.X = ReadSignedVarInt();
                        action.Y = ReadSignedVarInt();
                        action.Z = ReadSignedVarInt();
                        action.Facing = ReadVarInt();
                        break;
                    default:
                        break;
                }
            }
            return action;
        }

        public List<LegacySlot> ReadLegacySlots(int rawID)
        {
            bool hasLegacySlots = true;
            var legacySlots = new List<LegacySlot>();

            if (protocolVersion >= Info.v1_26_30)
            {
                 hasLegacySlots = ReadBool();
            }

            if (hasLegacySlots && rawID != 0)
            {
                int legacyCount = ReadVarInt();

                for (int i = 0; i < legacyCount; i++)
                {
                    LegacySlot legacySlot = new LegacySlot();
                    legacySlot.ContainerId = ReadByte();
                    for (int a = 0; a < ReadVarInt(); a++)
                    {
                        legacySlot.Slot[i] = ReadByte();
                    }
                    legacySlots.Add(legacySlot);
                }
            }

            return legacySlots;
        }

        public List<ItemStack> ReadItemStack()
        {
            var stacks = new List<ItemStack>();
            var size = ReadVarInt();

            for (int i = 0; i < size; i++)
            {
                var stack = new ItemStack();
                stack.RequestId = ReadVarInt();

                int actionCount = ReadVarInt();

                for (int a = 0; a < actionCount; a++)
                {
                    var actionType = (ItemStackRequestActionType)ReadByte();
                    Log.debug($"Reading action type: {actionType}");
                    switch (actionType)
                    {
                        case ItemStackRequestActionType.Take:
                            var take = new TakeAction();
                            take.ActionsType = actionType;
                            take.Amount = ReadByte();
                            take.Source = ReadSlotInfo();
                            take.Destination = ReadSlotInfo();
                            stack.Actions.Add(take);
                            break;
                        case ItemStackRequestActionType.Place:
                            var place = new PlaceAction();
                            place.ActionsType = actionType;
                            place.Amount = ReadByte();
                            place.Source = ReadSlotInfo();
                            place.Destination = ReadSlotInfo();
                            stack.Actions.Add(place);
                            break;
                        case ItemStackRequestActionType.Destroy:
                            var destory = new DestoryAction();
                            destory.ActionsType = actionType;
                            destory.Amount = ReadByte();
                            destory.Source = ReadSlotInfo();
                            stack.Actions.Add(destory);
                            break;
                        case ItemStackRequestActionType.CraftCreative:
                            var craftCreative = new CraftCreativeAction();
                            craftCreative.ActionsType = actionType;
                            craftCreative.ItemId = ReadVarInt();
                            craftCreative.CraftCount = ReadByte();
                            stack.Actions.Add(craftCreative);
                            break;
                        case ItemStackRequestActionType.CraftResults_DEPRECATEDASKTYLAING:
                            var CraftResultsDeprecated = new CraftResults_DEPRECATEDASKTYLAING();
                            CraftResultsDeprecated.ActionsType = actionType;
                            CraftResultsDeprecated.Items = ReadCraftResultsDeprecatedItems();
                            CraftResultsDeprecated.CraftCount = ReadByte();
                            stack.Actions.Add(CraftResultsDeprecated);
                            break;
                        default:
                            Log.error($"No implementation for action type {actionType}");
                            break;
                    }
                }
                int stringCount = ReadVarInt();
                for (int b = 0; b < stringCount; b++)
                {
                    stack.StringToFilter.Add(ReadString());
                }

                stack.StringToFilterOrigin = ReadInt();

                stacks.Add(stack);
            }
            return stacks;
        }

        public T? ReadOptional<T>(Func<T> readFunction)
        {
            return ReadBool() ? readFunction() : default;
        }

        public List<TEnum> Read<TEnum>() where TEnum : Enum
        {
            ulong value = (ulong)ReadVarLong();
            List<TEnum> result = new List<TEnum>();

            foreach (TEnum enumValue in Enum.GetValues(typeof(TEnum)))
            {
                int index = Convert.ToInt32(enumValue);
                if ((value & (1UL << index)) != 0)
                {
                    result.Add(enumValue);
                }
            }
            return result;
        }
    }

    public static class PacketDecoderPool
    {
        public static Stack<PacketDecoder> Pool = new Stack<PacketDecoder>();

        public static PacketDecoder Get(byte[] buffer, IPEndPoint clientEp)
        {
            if (Pool.Count > 0)
            {
                var session = RakSessionManager.getSession(clientEp);
                PacketDecoder decoder = Pool.Pop();
                decoder.Reset(buffer);
                decoder.clientEp = clientEp;
                decoder.player = Server.OnlinePlayers.ContainsKey(session.EntityID) ? Server.GetPlayer(session.EntityID) : null;
                decoder.protocolVersion = session.protocolVersion;
                return decoder;
            }
            else
            {
                return new PacketDecoder(buffer, clientEp);
            }
        }

        public static void Return(PacketDecoder decoder)
        {
            Pool.Push(decoder);
        }
    }
}
