/*
 * Copyright 2002-2015 Drew Noakes
 *
 *    Modified by Yakov Danilov <yakodani@gmail.com> for Imazen LLC (Ported from Java to C#)
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *
 *        http://www.apache.org/licenses/LICENSE-2.0
 *
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *
 * More information about this project is available at:
 *
 *    https://drewnoakes.com/code/exif/
 *    https://github.com/drewnoakes/metadata-extractor
 */

using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Icc;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Xmp;
using MetadataExtractor.IO;

namespace MetadataExtractor.Formats.Photoshop
{
    /// <summary>Reads metadata created by Photoshop and stored in the APPD segment of JPEG files.</summary>
    /// <remarks>
    /// Reads metadata created by Photoshop and stored in the APPD segment of JPEG files.
    /// Note that IPTC data may be stored within this segment, in which case this reader will
    /// create both a <see cref="PhotoshopDirectory"/> and a <see cref="IptcDirectory"/>.
    /// </remarks>
    /// <author>Yuri Binev</author>
    /// <author>Drew Noakes https://drewnoakes.com</author>
    public sealed class PhotoshopReader : IJpegSegmentMetadataReader
    {
        [NotNull]
        private const string JpegSegmentPreamble = "Photoshop 3.0";

        public IEnumerable<JpegSegmentType> GetSegmentTypes()
        {
            yield return JpegSegmentType.Appd;
        }

        public void ReadJpegSegments(IEnumerable<byte[]> segments, Metadata metadata, JpegSegmentType segmentType)
        {
            var preambleLength = JpegSegmentPreamble.Length;
            foreach (var segmentBytes in segments)
            {
                // Ensure data starts with the necessary preamble
                if (segmentBytes.Length < preambleLength + 1 || !JpegSegmentPreamble.Equals(Encoding.UTF8.GetString(segmentBytes, 0, preambleLength)))
                {
                    continue;
                }
                Extract(new SequentialByteArrayReader(segmentBytes, preambleLength + 1), segmentBytes.Length - preambleLength - 1, metadata);
            }
        }

        public void Extract([NotNull] SequentialReader reader, int length, [NotNull] Metadata metadata)
        {
            var directory = new PhotoshopDirectory();
            metadata.AddDirectory(directory);
            // Data contains a sequence of Image Resource Blocks (IRBs):
            //
            // 4 bytes - Signature "8BIM"
            // 2 bytes - Resource identifier
            // String  - Pascal string, padded to make length even
            // 4 bytes - Size of resource data which follows
            // Data    - The resource data, padded to make size even
            //
            // http://www.adobe.com/devnet-apps/photoshop/fileformatashtml/#50577409_pgfId-1037504
            var pos = 0;
            while (pos < length)
            {
                try
                {
                    // 4 bytes for the signature.  Should always be "8BIM".
                    var signature = reader.GetString(4);
                    if (!signature.Equals("8BIM"))
                    {
                        throw new ImageProcessingException("Expecting 8BIM marker");
                    }
                    pos += 4;
                    // 2 bytes for the resource identifier (tag type).
                    var tagType = reader.GetUInt16();
                    // segment type
                    pos += 2;
                    // A variable number of bytes holding a pascal string (two leading bytes for length).
                    var descriptionLength = reader.GetUInt8();
                    pos += 1;
                    // Some basic bounds checking
                    if (descriptionLength < 0 || descriptionLength + pos > length)
                    {
                        throw new ImageProcessingException("Invalid string length");
                    }
                    // We don't use the string value here
                    reader.Skip(descriptionLength);
                    pos += descriptionLength;
                    // The number of bytes is padded with a trailing zero, if needed, to make the size even.
                    if (pos % 2 != 0)
                    {
                        reader.Skip(1);
                        pos++;
                    }
                    // 4 bytes for the size of the resource data that follows.
                    var byteCount = reader.GetInt32();
                    pos += 4;
                    // The resource data.
                    var tagBytes = reader.GetBytes(byteCount);
                    pos += byteCount;
                    // The number of bytes is padded with a trailing zero, if needed, to make the size even.
                    if (pos % 2 != 0)
                    {
                        reader.Skip(1);
                        pos++;
                    }
                    if (tagType == PhotoshopDirectory.TagIptc)
                    {
                        new IptcReader().Extract(new SequentialByteArrayReader(tagBytes), metadata, tagBytes.Length);
                    }
                    else
                    {
                        if (tagType == PhotoshopDirectory.TagIccProfileBytes)
                        {
                            new IccReader().Extract(new ByteArrayReader(tagBytes), metadata);
                        }
                        else
                        {
                            if (tagType == PhotoshopDirectory.TagExifData1 || tagType == PhotoshopDirectory.TagExifData3)
                            {
                                new ExifReader().Extract(new ByteArrayReader(tagBytes), metadata);
                            }
                            else
                            {
                                if (tagType == PhotoshopDirectory.TagXmpData)
                                {
                                    new XmpReader().Extract(tagBytes, metadata);
                                }
                                else
                                {
                                    directory.SetByteArray(tagType, tagBytes);
                                }
                            }
                        }
                    }
                    if (tagType >= unchecked(0x0fa0) && tagType <= unchecked(0x1387))
                    {
                        PhotoshopDirectory.TagNameMap[tagType] = string.Format("Plug-in {0} Data", tagType - unchecked(0x0fa0) + 1);
                    }
                }
                catch (Exception ex)
                {
                    directory.AddError(ex.Message);
                    return;
                }
            }
        }
    }
}