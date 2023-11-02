using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GNU_MO_File_Editor
{
	/**
	 * Data structures
	 */
	struct MOLine
	{
		public int Index;
		public int SubIndex;
		public string Original;
		public string Translated;
	}

	/**
	 * Reader class for the GNU .mo text format
	 * 
	 * https://www.gnu.org/software/gettext/manual/html_node/MO-Files.html#MO-Files
	 * 
	 */
	class MOReader: IEnumerable<MOLine>
	{
		#region privates
		private uint _r; // revision
		private uint _n; // number of strings
		private uint _o; // offset of table with original strings
		private uint _t; // offset of table with translation strings
		private uint _s; // size of hashing table
		private uint _h; // offset of hashing table

		private const int offsetTextTable = 0x1C;
		private const uint TextEntrySize = 8;
		private const uint HashEntrySize = 4;

		private uint[] _originalStringOffsets;
		private int[] _originalStringLengths;

		private uint[] _translatedStringOffsets;
		private int[] _translatedStringLengths;

		private byte[] _hashTable;

		protected readonly string FileName;
		protected readonly FileStream MOFile;
		protected readonly BinaryReader Reader;

		protected List<MOLine> Lines;
		#endregion

		// public fields
		public uint Count
		{
			get { return (uint)(Lines?.Count ?? 0); }
		}

		// constructor
		public MOReader(string fileName)
		{
			FileName = fileName;
			MOFile = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);

			Reader = new BinaryReader(MOFile);

			PopulateDataStructures();
		}

		// destructor
		~MOReader()
		{
			if (Reader != null)
				Reader.Close();

			if (MOFile != null)
			{
				MOFile.Close();
				MOFile.Dispose();
			}
		}

		#region Format Table
		/*
	   byte
             +------------------------------------------+
          0  | magic number = 0x950412de                |
             |                                          |
          4  | file format revision = 0                 |
             |                                          |
          8  | number of strings                        |  == N
             |                                          |
         12  | offset of table with original strings    |  == O
             |                                          |
         16  | offset of table with translation strings |  == T
             |                                          |
         20  | size of hashing table                    |  == S
             |                                          |
         24  | offset of hashing table                  |  == H
             |                                          |
             .                                          .
             .    (possibly more entries later)         .
             .                                          .
             |                                          |
          O  | length & offset 0th string  ----------------.
      O + 8  | length & offset 1st string  ------------------.
              ...                                    ...   | |
O + ((N-1)*8)| length & offset (N-1)th string           |  | |
             |                                          |  | |
          T  | length & offset 0th translation  ---------------.
      T + 8  | length & offset 1st translation  -----------------.
              ...                                    ...   | | | |
T + ((N-1)*8)| length & offset (N-1)th translation      |  | | | |
             |                                          |  | | | |
          H  | start hash table                         |  | | | |
              ...                                    ...   | | | |
  H + S * 4  | end hash table                           |  | | | |
             |                                          |  | | | |
             | NUL terminated 0th string  <----------------' | | |
             |                                          |    | | |
             | NUL terminated 1st string  <------------------' | |
             |                                          |      | |
              ...                                    ...       | |
             |                                          |      | |
             | NUL terminated 0th translation  <---------------' |
             |                                          |        |
             | NUL terminated 1st translation  <-----------------'
             |                                          |
              ...                                    ...
             |                                          |
             +------------------------------------------+
		 */
		#endregion

		#region private methods
		private void PopulateDataStructures()
		{
			// start at the beginnings
			Reader.BaseStream.Seek(0, SeekOrigin.Begin);

			uint magic = Reader.ReadUInt32();

			if (magic != 0x950412de)
				throw new FormatException(string.Format("File {0} is not a valid GNU .mo file!", FileName));

			_r = Reader.ReadUInt32(); // revision
			_n = Reader.ReadUInt32(); // number of strings
			_o = Reader.ReadUInt32(); // offset of table with original strings
			_t = Reader.ReadUInt32(); // offset of table with translation strings
			_s = Reader.ReadUInt32(); // size of hashing table
			_h = Reader.ReadUInt32(); // offset of hashing table

			// get the original string offsets and lengths
			_originalStringOffsets = new uint[_n];
			_originalStringLengths = new int[_n];

			Reader.BaseStream.Seek(_o, SeekOrigin.Begin);

			for (uint i = 0; i < _n; i++)
			{
				// length & offset i-th string
				_originalStringLengths[i] = Reader.ReadInt32();
				_originalStringOffsets[i] = Reader.ReadUInt32();
			}

			// get the translated string offsets and lengths
			_translatedStringOffsets = new uint[_n];
			_translatedStringLengths = new int[_n];

			Reader.BaseStream.Seek(_t, SeekOrigin.Begin);

			for (uint i = 0; i < _n; i++)
			{
				// length & offset i-th translation
				_translatedStringLengths[i] = Reader.ReadInt32();
				_translatedStringOffsets[i] = Reader.ReadUInt32();
			}

			Reader.BaseStream.Seek(_h, SeekOrigin.Begin);

			_hashTable = Reader.ReadBytes((int)(_s*HashEntrySize));

			PopulateLines();
		}

		protected string GetStringAt(uint offset, int length)
		{
			Reader.BaseStream.Seek(offset, SeekOrigin.Begin);
			byte[] stringBytes = Reader.ReadBytes(length);

			return Encoding.UTF8.GetString(stringBytes);
		}

		protected MOLine[] ReadLineAt(int index)
		{
			var lines = new List<MOLine>();

			var keyLine = GetStringAt(_originalStringOffsets[index], _originalStringLengths[index]);
			var textLine = GetStringAt(_translatedStringOffsets[index], _translatedStringLengths[index]);

			if (keyLine.IndexOf('\0') > 0)
            {
				var keyLines = keyLine.Split('\0');
				var textLines = textLine.Split('\0');
				if (keyLines.Length != textLines.Length)
				{
					throw new FormatException("Processing error, keys count is not match text counts");
				}

                for (int i = 0; i < keyLines.Length; i++)
                {
					lines.Add(new MOLine()
					{
						Index = index,
						SubIndex = i,
						Original = keyLines[i],
						Translated = textLines[i],
					});
				}
			}
			else
            {
				lines.Add(new MOLine()
				{
					Index = index,
					SubIndex = -1,
					Original = keyLine,
					Translated = textLine,
				});
			}


			return lines.ToArray();
		}

        internal void Add(MOLine item)
        {
			Lines.Add(item);
		}

        protected void PopulateLines()
		{
			Lines = new List<MOLine>();

			for (int i = 0; i < _n; i++)
				Lines.AddRange(ReadLineAt(i));
		}
		#endregion

		#region data writer methods
		public void SaveMOFile(string fileName)
		{
			FileStream outFile = File.Open(fileName, FileMode.Create, FileAccess.Write, FileShare.Read);
			BinaryWriter writer = new BinaryWriter(outFile);

			// magic
			writer.Write(0x950412de);

			// revision
			writer.Write(_r);

			uint packCount = (uint)Lines.Select(c => c.Index).Distinct().Count();

			// number of strings
			writer.Write(packCount);

			// offset of table with original strings
			writer.Write(offsetTextTable);

			// offset of table with translation strings
			writer.Write(packCount * TextEntrySize + offsetTextTable);

			// size of hashing table
			writer.Write(_s);

			// offset of hashing table
			uint hashOffset = packCount * TextEntrySize * 2 + offsetTextTable;
			writer.Write(hashOffset);
			
			// offsets and lengths
			int[] osLengths = new int[packCount];
			uint[] osOffsets = new uint[packCount];

			int[] tsLengths = new int[packCount];
			uint[] tsOffsets = new uint[packCount];

			// get original string offsets
			uint osOffs = hashOffset + _s*HashEntrySize;
			uint tsOffs = 0;
			for (int i = 0; i < packCount; i++)
			{
				var lines = Lines.Where(c => c.Index == i).OrderBy(c => c.SubIndex).ToList();
				string lineKey = lines.Aggregate("", (prev, line) => { return prev + (prev.Length > 0 ? "\0":"") + line.Original; });
				string lineValue = lines.Aggregate("", (prev, line) => { return prev + (prev.Length > 0 ? "\0":"") + line.Translated; });

				osLengths[i] = Encoding.UTF8.GetBytes(lineKey).Length;
				tsLengths[i] = Encoding.UTF8.GetBytes(lineValue).Length;

				//if (tsLengths[i] != _translatedStringLengths[i])
				//	throw new Exception(string.Format("Invalid lengths: original={0}, new={1}", tsLengths[i], _translatedStringLengths[i]));

				osOffsets[i] = osOffs;

				osOffs += (uint)osLengths[i] + 1;
				tsOffs = osOffs;

				// write original string offsets
				writer.Write(osLengths[i]);
				writer.Write(osOffsets[i]);
			}

			// get translated string offsets
			for (int i = 0; i < packCount; i++)
			{
				tsOffsets[i] = tsOffs;

				tsOffs += 1 + (uint)tsLengths[i];

				// write ranslated string offsets
				writer.Write(tsLengths[i]);
				writer.Write(tsOffsets[i]);
			}

			// dump original hash table
			writer.Write(_hashTable);

			// write original strings
			for (int i = 0; i < packCount; i++)
			{
				var lines = Lines.Where(c => c.Index == i).OrderBy(c => c.SubIndex).ToList();
				string lineKey = lines.Aggregate("", (prev, line) => { return prev + (prev.Length > 0 ? "\0" : "") + line.Original; });

				byte[] stringBytes = Encoding.UTF8.GetBytes(lineKey);

				writer.Write(stringBytes);
				writer.Write((byte)0);
			}

			// write translated strings
			for (int i = 0; i < packCount; i++)
			{
				var lines = Lines.Where(c => c.Index == i).OrderBy(c => c.SubIndex).ToList();
				string lineValue = lines.Aggregate("", (prev, line) => { return prev + (prev.Length > 0 ? "\0" : "") + line.Translated; });

				byte[] stringBytes = Encoding.UTF8.GetBytes(lineValue);

				writer.Write(stringBytes);
				writer.Write((byte)0);
			}

			// close and cleanup
			writer.Close();
			outFile.Close();
			outFile.Dispose();
		}
		#endregion

		#region Enumerator and Indexer shenanigans
		public MOLine this[int key]
		{
			get
			{
				return Lines[key];
			}
			set
			{
				Lines[key] = value;
			}
		}

		public IEnumerator<MOLine> GetEnumerator()
		{
			return Lines.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
		#endregion
	}
}
