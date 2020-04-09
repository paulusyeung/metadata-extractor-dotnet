﻿using MetadataExtractor.IO;

namespace MetadataExtractor.Formats.Heif.Iso14496Parser
{
    public class ColorInformationBox : Box
    {
        public uint ColorType { get; }
        public ushort ColorPrimaries { get; }
        public ushort TransferCharacteristics { get; }
        public ushort MatrixCharacteristics { get; }
        public bool FullRangeFlag { get; }
        public byte[] IccProfile { get; }
        private const uint NclxTag = 0x6E636C78; // nclx
        private const uint RICCTag = 0x72494343; // rICC
        private const uint ProfTag = 0x70726F66; // prof
        public ColorInformationBox(BoxLocation location, SequentialReader sr) : base(location)
        {
            ColorType = sr.GetUInt32();
            switch (ColorType)
            {
                case NclxTag:
                    ColorPrimaries = sr.GetUInt16();
                    TransferCharacteristics = sr.GetUInt16();
                    MatrixCharacteristics = sr.GetUInt16();
                    FullRangeFlag = (sr.GetByte() & 128) == 128;
                    IccProfile = new byte[0];
                    break;
                case RICCTag:
                case ProfTag:
                    IccProfile = ReadRemainingData(sr);
                    break;
                default:
                    IccProfile = new byte[0];
                    break;
            }
        }
    }
}