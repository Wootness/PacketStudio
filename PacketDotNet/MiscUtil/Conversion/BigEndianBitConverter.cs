using System;

namespace PacketDotNet.MiscUtil.Conversion
{
    /// <summary>
    /// Implementation of EndianBitConverter which converts to/from big-endian
    /// byte arrays.
    /// </summary>
    public sealed class BigEndianBitConverter : EndianBitConverter
    {
        /// <summary>
        /// Indicates the byte order ("endianess") in which data is converted using this class.
        /// </summary>
        public override Endianness Endianness => Endianness.BigEndian;

        /// <summary>
        /// Indicates the byte order ("endianess") in which data is converted using this class.
        /// </summary>
        /// <remarks>
        /// Different computer architectures store data using different byte orders. "Big-endian"
        /// means the most significant byte is on the left end of a word. "Little-endian" means the
        /// most significant byte is on the right end of a word.
        /// </remarks>
        /// <returns>true if this converter is little-endian, false otherwise.</returns>
        public override Boolean IsLittleEndian()
        {
            return false;
        }

        /// <summary>
        /// Copies the specified number of bytes from value to buffer, starting at index.
        /// </summary>
        /// <param name="value">The value to copy</param>
        /// <param name="bytes">The number of bytes to copy</param>
        /// <param name="buffer">The buffer to copy the bytes into</param>
        /// <param name="index">The index to start at</param>
        protected override void CopyBytesImpl(Int64 value, Int32 bytes, Byte[] buffer, Int32 index)
        {
            var endOffset = index + bytes - 1;
            for (var i = 0; i < bytes; i++)
            {
                buffer[endOffset - i] = unchecked((Byte) (value & 0xff));
                value = value >> 8;
            }
        }

        /// <summary>
        /// Returns a value built from the specified number of bytes from the given buffer,
        /// starting at index.
        /// </summary>
        /// <param name="buffer">The data in byte array format</param>
        /// <param name="startIndex">The first index to use</param>
        /// <param name="bytesToConvert">The number of bytes to use</param>
        /// <returns>The value built from the given bytes</returns>
        protected override Int64 FromBytes(Byte[] buffer, Int32 startIndex, Int32 bytesToConvert)
        {
            Int64 ret = 0;
            for (var i = 0; i < bytesToConvert; i++)
            {
                ret = unchecked((ret << 8) | buffer[startIndex + i]);
            }

            return ret;
        }
    }
}