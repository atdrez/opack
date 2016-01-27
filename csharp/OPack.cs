/*
 * Copyright (c) 2016 Adriano Tinoco d'Oliveira Rezende
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this software
 * and associated documentation files (the "Software"), to deal in the Software without restriction,
 * including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all copies or
 * substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
 * INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
 * PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
 * FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 *
 */

using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace OPack
{
	class IndexTable
	{
		public enum Type
		{
			Tiny = 1, // 1-byte length indexes
			Small = 2, // 2-byte length indexes
			Normal = 3, // 4-byte length indexes
		}

		public Type type;
		public List<object> entries;

		public static IndexTable FromCollection(ICollection<object> items)
		{
			var result = new IndexTable ();
			result.entries = new List<object> ();

			if (items.Count <= byte.MaxValue)
				result.type = Type.Tiny;
			else if (items.Count <= UInt16.MaxValue)
				result.type = Type.Small;
			else
				result.type = Type.Normal;

			foreach (var value in items)
				result.entries.Add (value);

			return result;
		}

		public static IndexTable[] FromCollection(ICollection<object> items, int maxSplit)
		{
			if (items.Count == 0)
				return null;

			int bucketCount = (int)Math.Ceiling(items.Count / (float)byte.MaxValue);

			if (bucketCount == 1 || bucketCount > maxSplit)
				return new IndexTable[] { FromCollection (items) };

			var keyList = new List<object>(items);
			var result = new IndexTable[bucketCount];

			for (int i = 0, k = 0; i < keyList.Count; i += byte.MaxValue, k++) {
				int end = i + byte.MaxValue;
				int count = Math.Min(end, keyList.Count) - i;

				result[k] = FromCollection(keyList.GetRange(i, count));
			}

			return result;
		}
	}

	enum ElementType
	{
		None = 0,

		// basic types
		Null = 1,
		True = 2,
		False = 3,
		Indexed = 4, // indexed values

		// Map of objects [32, 16, 8] bits address
		Map8 = 5,
		Map16 = 6,
		Map32 = 7,

		// Array of objects [32, 16, 8] bits address
		Array8 = 8,
		Array16 = 9,
		Array32 = 10,

		// signed int types
		Int8 = 11,
		Int16 = 12,
		Int32 = 13,
		Int64 = 14,

		// unsigned int types
		UInt8 = 15,
		UInt16 = 16,
		UInt32 = 17,
		UInt64 = 18,

		// floating point types
		Float = 19,
		Double = 20,

		// Strings 2^[8, 16, 32]-1 length
		String8 = 21,
		String16 = 22,
		String32 = 23,

		DateTime = 24,

		// String5: Strings up to 2^5-1 elements
		// BitMask: 0b011XXXXX, index range [0x60, 0x7F]
		String5First = 0x60, String5Last = 0x97F,

		// Map5: Maps up to 2^5-1 elements
		// BitMask: 0b100XXXXX, index range [0x80, 0x9F]
		Map5First = 0x80, Map5Last = 0x9F,

		// Arrays up to 2^5-1 elements
		// BitMask: 0b101XXXXX, index range [0xA0, BF]
		Array5First = 0xA0, Array5Last = 0xBF,

		// Indexes that addresses up to 2^5-1 tables
		// BitMask: 0b110XXXXX, index range [0xC0, 0xDF]
		Indexed5First = 0xC0, Indexed5Last = 0xDF,
	}

	public class OPack
	{
		const int MaskBit8 = 0x80;
		const int Mask5Bits = 0x1F;
		const int Mask7Bits = 0x7F;
		const int MaxSpecialTables = 32;

		public bool indexValues {
			get;
			set;
		}

		public int maxTableSplitForKey {
			get;
			set;
		}

		public int maxTableSplitForValue {
			get;
			set;
		}

		public bool writeOptimizedIndexedTypes {
			get;
			set;
		}

		BinaryReader mReader;
		BinaryWriter mWriter;
		List<string> mKeyList;
		List<IndexTable> mIndexTables;

		public OPack()
		{
			indexValues = true;
			maxTableSplitForKey = 100;
			maxTableSplitForValue = 150;
			writeOptimizedIndexedTypes = true;
		}

		public byte[] Encode(object data)
		{
			using (var stream = new MemoryStream()) {
				using (mWriter = new BinaryWriter(stream)) {
					EncodeDocument (data);

					byte[] result = new byte[stream.Position];
					stream.Seek(0, SeekOrigin.Begin);
					stream.Read (result, 0, result.Length);

					return result;
				}
			}
		}

		public object Decode(byte[] data)
		{
			using (var stream = new MemoryStream(data)) {
				using (mReader = new BinaryReader(stream)) {
					return DecodeDocument (data);
				}
			}
		}

		private void EncodeDocument(object data)
		{
			var keySet = new HashSet<object>();
			var valueSet = new HashSet<object>();

			// fill index tables
			FillIndexTables (data, keySet, valueSet, indexValues);

			// TODO: make it dynamic
			mIndexTables = new List<IndexTable>();

			var keyTables = IndexTable.FromCollection (keySet, maxTableSplitForKey);
			var valueTables = IndexTable.FromCollection (valueSet, maxTableSplitForValue);

			if (keyTables != null)
				mIndexTables.AddRange (keyTables);

			if (valueTables != null)
				mIndexTables.AddRange (valueTables);

			// encode index tables
			mWriter.Write ((byte)mIndexTables.Count);

			for (int i = 0; i < mIndexTables.Count; i++)
				EncodeIndexTable (mIndexTables[i]);

			EncodeElement (data);
		}

		private object DecodeDocument(byte[] data)
		{
			uint tableCount = mReader.ReadByte ();

			mIndexTables = new List<IndexTable>();

			for (uint i = 0; i < tableCount; i++)
				mIndexTables.Add(ReadIndexTable());

			return DecodeElement ();
		}

		private IndexTable ReadIndexTable()
		{
			uint count;
			var indexType = (IndexTable.Type)mReader.ReadByte ();

			switch (indexType) {
			case IndexTable.Type.Tiny:
				count = mReader.ReadByte();
				break;
			case IndexTable.Type.Small:
				count = mReader.ReadUInt16();
				break;
			case IndexTable.Type.Normal:
				count = mReader.ReadUInt32();
				break;
			default:
				throw new Exception("Invalid index table type found");
			}

			var table = new IndexTable ();
			table.type = indexType;
			table.entries = new List<object>();

			for (ulong i = 0; i < count; i++)
				table.entries.Add(DecodeElement()); // XXX: do not consider nested elements

			return table;
		}

		private void EncodeElement(object value, bool lookup = true)
		{
			if (value == null) {
				mWriter.Write((byte)ElementType.Null);
				return;
			}

			var type = value.GetType();

			if (value is IDictionary<string, object>) {
				EncodeObject (value as IDictionary<string, object>);
				return;
			}

			if (value is IList<object>) {
				EncodeArray (value as IList<object>);
				return;
			}

			if (type == typeof(bool)) {
				if ((bool)value)
					mWriter.Write((byte)ElementType.True);
				else
					mWriter.Write((byte)ElementType.False);
				return;
			}

			if (type == typeof(sbyte)) {
				mWriter.Write((byte)ElementType.Int8);
				mWriter.Write((sbyte)value);
				return;
			}

			if (type == typeof(byte)) {
				mWriter.Write((byte)ElementType.UInt8);
				mWriter.Write((byte)value);
				return;
			}

			if (lookup && indexValues) {
				// encode indexed values
				if (!EncodeIndexedValue(value))
					throw new Exception("Indexed value not found in tables: " +
					                    value.ToString() + ", type: " + type.ToString ());
				return;
			}

			// encode strings
			if (value is string) {
				var str = value as string;
				EncodeTypeWithPrecision(ElementType.String32, str.Length);

				if (str.Length > 0)
					mWriter.Write(new UTF8Encoding ().GetBytes (str));
				return;
			}

			// XXX: datetime
			if (type == typeof(DateTime)) {
				mWriter.Write((byte)ElementType.DateTime);
				mWriter.Write(((DateTime)value).ToFileTimeUtc());
				return;
			}

			// floating points
			if (type == typeof(Single)) {
				mWriter.Write((byte)ElementType.Float);
				mWriter.Write((Single)value);
				return;
			}
			if (type == typeof(Double)) {
				mWriter.Write((byte)ElementType.Double);
				mWriter.Write((Double)value);
				return;
			}

			// signed ints
			if (type == typeof(Int16)) {
				mWriter.Write((byte)ElementType.Int16);
				mWriter.Write((Int16)value);
				return;
			}
			if (type == typeof(Int32)) {
				mWriter.Write((byte)ElementType.Int32);
				mWriter.Write((Int32)value);
				return;
			}
			if (type == typeof(Int64)) {
				mWriter.Write((byte)ElementType.Int64);
				mWriter.Write((Int64)value);
				return;
			}

			// unsigned ints
			if (type == typeof(UInt16)) {
				mWriter.Write((byte)ElementType.UInt16);
				mWriter.Write((UInt16)value);
				return;
			}
			if (type == typeof(UInt32)) {
				mWriter.Write((byte)ElementType.UInt32);
				mWriter.Write((UInt32)value);
				return;
			}
			if (type == typeof(UInt64)) {
				mWriter.Write((byte)ElementType.UInt64);
				mWriter.Write((UInt64)value);
				return;
			}

			throw new Exception ("Invalid type found while encoding: " + type.ToString());
		}

		private object DecodeElement()
		{
			var type = (ElementType)mReader.ReadByte();

			switch (type) {
			// null object
			case ElementType.Null:
				return null;

			// generic objects
			case ElementType.Map32:
			case ElementType.Map8:
			case ElementType.Map16:
				return DecodeObject(type);

			// arrays
			case ElementType.Array32:
			case ElementType.Array8:
			case ElementType.Array16:
				return DecodeArray(type);

			// indexed values
			case ElementType.Indexed:
				return DecodeIndexedValue();

			// string types
			case ElementType.String32:
			case ElementType.String8:
			case ElementType.String16:
				return DecodeString(type);

			// basic types
			case ElementType.True:
				return true;
			case ElementType.False:
				return false;
			case ElementType.DateTime: // XXX: check format
				return DateTime.FromFileTimeUtc(mReader.ReadInt64());

			// signed ints
			case ElementType.Int8:
				return mReader.ReadSByte();
			case ElementType.Int16:
				return mReader.ReadInt16();
			case ElementType.Int32:
				return mReader.ReadInt32();
			case ElementType.Int64:
				return mReader.ReadInt64();

			// unsigned ints
			case ElementType.UInt8:
				return mReader.ReadByte();
			case ElementType.UInt16:
				return mReader.ReadUInt16();
			case ElementType.UInt32:
				return mReader.ReadUInt32();
			case ElementType.UInt64:
				return mReader.ReadUInt64();

			// floating point
			case ElementType.Float:
				return mReader.ReadSingle();
			case ElementType.Double:
				return mReader.ReadDouble();

			default:
				// special indexed values
				if (type >= ElementType.Indexed5First && type <= ElementType.Indexed5Last) {
					int tableIndex = (byte)type - (byte)ElementType.Indexed5First;
					return DecodeIndexedValueInTable(tableIndex);
				}

				if (type >= ElementType.Map5First && type <= ElementType.Map5Last)
					return DecodeObject(type);

				if (type >= ElementType.Array5First && type <= ElementType.Array5Last)
					return DecodeArray(type);

				if (type >= ElementType.String5First && type <= ElementType.String5Last)
					return DecodeString(type);

				throw new Exception("Invalid element type found: " + (byte) type);
			}
		}

		private object DecodeString(ElementType type)
		{
			int length = (int)DecodeLengthForType (type);

			if (length == 0) {
				return string.Empty;
			} else {
				byte[] buffer = mReader.ReadBytes (length);
				return Encoding.UTF8.GetString (buffer, 0, length);
			}
		}

		private void EncodeObject(IDictionary<string, object> values)
		{
			EncodeTypeWithPrecision (ElementType.Map32, values.Count);

			foreach (var entry in values) {
				// write indexed key
				if (!EncodeIndexedKey(entry.Key))
					throw new InvalidOperationException("Key was not found in tables");

				// write value
				EncodeElement(entry.Value);
			}
		}

		private object DecodeObject(ElementType type)
		{
			uint count = DecodeLengthForType (type);
			var result = new Dictionary<string, object> ();

			for (uint i = 0; i < count; i++) {
				var key = DecodeIndexedKey ();
				result.Add (key, DecodeElement ());
			}

			return result;
		}

		private void EncodeArray(IList<object> values)
		{
			EncodeTypeWithPrecision(ElementType.Array32, values.Count);

			for (int i = 0; i < values.Count; i++)
				EncodeElement (values[i]);
		}

		private object DecodeArray(ElementType type)
		{
			uint count = DecodeLengthForType (type);
			var result = new List<object> ();

			for (uint i = 0; i < count; i++)
				result.Add(DecodeElement());

			return result;
		}

		private bool EncodeIndexedKey(object value)
		{
			int tableIndex, elementIndex;

			if (!FindValueInTables (value, out tableIndex, out elementIndex))
				return false;

			// if table index = 0 and element index in range 0b0XXXXXXX
			// then just pack element index position into one byte
			if (tableIndex == 0 && elementIndex <= Mask7Bits) {
				mWriter.Write((byte)elementIndex);
			} else {
				if (tableIndex > Mask7Bits)
					throw new Exception("Key must have table index less than 128");

				// Write 0b1TTTTTTT <element-index>   (T = table index)
				mWriter.Write((byte)((tableIndex & Mask7Bits) | MaskBit8));
				EncodeIndexedValueOnly(value, tableIndex, elementIndex, false);
			}

			return true;
		}

		private bool EncodeIndexedValue(object value)
		{
			int tableIndex, elementIndex;

			if (!FindValueInTables (value, out tableIndex, out elementIndex))
				return false;

			if (writeOptimizedIndexedTypes && tableIndex < MaxSpecialTables) {
				mWriter.Write((byte)(tableIndex + (byte)ElementType.Indexed5First));
				EncodeIndexedValueOnly(value, tableIndex, elementIndex, false);
			} else {
				mWriter.Write((byte)ElementType.Indexed);
				EncodeIndexedValueOnly(value, tableIndex, elementIndex, true);
			}

			return true;
		}

		private bool EncodeIndexedValueOnly(object value, bool writeTableIndex = true)
		{
			int tableIndex, elementIndex;

			if (!FindValueInTables (value, out tableIndex, out elementIndex))
				return false;

			EncodeIndexedValueOnly (value, tableIndex, elementIndex, writeTableIndex);
			return true;
		}

		private void EncodeIndexedValueOnly(object value, int tableIndex,
		                                    int elementIndex, bool writeTableIndex)
		{
			var table = mIndexTables[tableIndex];

			if (writeTableIndex)
				mWriter.Write ((byte)tableIndex);

			if (table.type == IndexTable.Type.Tiny)
				mWriter.Write((byte)elementIndex);
			else if (table.type == IndexTable.Type.Small)
				mWriter.Write((UInt16)elementIndex);
			else
				mWriter.Write((UInt32)elementIndex);
		}

		private string DecodeIndexedKey()
		{
			byte position = mReader.ReadByte();

			if ((position & MaskBit8) == 0) {
				// element in first table
				var table = mIndexTables[0];
				return table.entries[position] as string;
			} else {
				// get table:elementindex tuple
				int tableIndex = (int)(position & Mask7Bits);
				return DecodeIndexedValueInTable (tableIndex) as string;
			}
		}

		private object DecodeIndexedValue()
		{
			int tableIndex = (int)mReader.ReadByte();
			return DecodeIndexedValueInTable (tableIndex);
		}

		private object DecodeIndexedValueInTable(int tableIndex)
		{
			if (tableIndex >= mIndexTables.Count) {
				throw new Exception (string.Format ("Invalid table index found {0} (tables: {1})",
				                                  tableIndex, mIndexTables.Count));
			}

			int elementIndex;
			var table = mIndexTables[tableIndex];

			if (table.type == IndexTable.Type.Tiny)
				elementIndex = mReader.ReadByte();
			else if (table.type == IndexTable.Type.Small)
				elementIndex = mReader.ReadUInt16();
			else
				elementIndex = (int)mReader.ReadUInt32();

			return table.entries[elementIndex];
		}

		private void EncodeIndexTable(IndexTable table)
		{
			int count = table.entries.Count;

			if (count <= byte.MaxValue) {
				mWriter.Write ((byte)IndexTable.Type.Tiny);
				mWriter.Write((byte) count);
			} else if (count <= UInt16.MaxValue) {
				mWriter.Write ((byte)IndexTable.Type.Small);
				mWriter.Write((UInt16) count);
			} else {
				mWriter.Write ((byte)IndexTable.Type.Normal);
				mWriter.Write((UInt32) count);
			}

			foreach (var value in table.entries) {
				EncodeElement (value, false);
			}
		}

		private void FillIndexTables(object value,
		                             HashSet<object> keySet,
		                             HashSet<object> valueSet,
		                             bool indexValues = true)
		{
			// Do not index null values
			if (value == null)
				return;

			// Do not index 1-byte types
			var t = value.GetType ();
			if (t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte))
				return;

			if (value is IDictionary<string, object>) {
				var dict = value as IDictionary<string, object>;

				foreach (var entry in dict) {
					keySet.Add (entry.Key);
					FillIndexTables(entry.Value, keySet, valueSet, indexValues);
				}
			} else if (value is IList<object>) {
				var lst = value as IList<object>;

				foreach (var obj in lst)
					FillIndexTables (obj, keySet, valueSet, indexValues);
			} else {
				if (indexValues)
					valueSet.Add(value);
			}
		}

		private bool FindKeyInTables(string key, out int tableIndex, out int elementIndex)
		{
			return FindValueInTables (key, out tableIndex, out elementIndex);
		}

		private bool FindValueInTables(object value, out int tableIndex, out int elementIndex)
		{
			tableIndex = -1;
			elementIndex = -1;

			for (int i = 0; i < mIndexTables.Count; i++) {
				int index = mIndexTables[i].entries.IndexOf(value);

				if (index >= 0) {
					tableIndex = i;
					elementIndex = index;
					return true;
				}
			}

			return false;
		}

		private void EncodeTypeWithPrecision(ElementType type, int count)
		{
			if (count <= Mask5Bits) {
				switch (type) {
				case ElementType.Array32:
					type = (ElementType)((byte)ElementType.Array5First + count);
					break;
				case ElementType.Map32:
					type = (ElementType)((byte)ElementType.Map5First + count);
					break;
				case ElementType.String32:
					type = (ElementType)((byte)ElementType.String5First + count);
					break;
				default:
					throw new InvalidOperationException ("Invalid use of EncodeTypeWithPrecision");
				}

				mWriter.Write ((byte)type);
			} else if (count <= byte.MaxValue) {
				switch (type) {
				case ElementType.Array32:
					type = ElementType.Array8;
					break;
				case ElementType.Map32:
					type = ElementType.Map8;
					break;
				case ElementType.String32:
					type = ElementType.String8;
					break;
				default:
					throw new InvalidOperationException ("Invalid use of EncodeTypeWithPrecision");
				}

				mWriter.Write ((byte)type);
				mWriter.Write ((byte)count);
			} else if (count <= UInt16.MaxValue) {
				switch (type) {
				case ElementType.Array32:
					type = ElementType.Array16;
					break;
				case ElementType.Map32:
					type = ElementType.Map16;
					break;
				case ElementType.String32:
					type = ElementType.String16;
					break;
				default:
					throw new InvalidOperationException ("Invalid use of EncodeTypeWithPrecision");
				}

				mWriter.Write ((byte)type);
				mWriter.Write ((UInt16)count);
			} else {
				mWriter.Write ((byte)type);
				mWriter.Write ((UInt32)count);
			}
		}

		private uint DecodeLengthForType(ElementType type)
		{
			switch (type) {
			case ElementType.Array8:
			case ElementType.Map8:
			case ElementType.String8:
				return mReader.ReadByte();
			case ElementType.Array16:
			case ElementType.Map16:
			case ElementType.String16:
				return mReader.ReadUInt16();
			case ElementType.Array32:
			case ElementType.Map32:
			case ElementType.String32:
				return mReader.ReadUInt32();
			default:
				// Map5, Array5, String5 types
				if ((type >= ElementType.Map5First && type <= ElementType.Map5Last) |
				    (type >= ElementType.Array5First && type <= ElementType.Array5Last) |
				    (type >= ElementType.String5First && type <= ElementType.String5Last))
					return (uint)((byte)type & Mask5Bits);

				throw new InvalidOperationException ("Invalid use of DecodeLengthForType");
			}
		}
	}
}
