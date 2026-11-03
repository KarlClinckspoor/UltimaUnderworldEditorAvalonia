using System;
using System.Collections.Generic;

namespace UltimateEditor.Sound
{
	/// <summary>A timbre of a global timbre library: its patch and bank, and its bytes (the size word first).</summary>
	public sealed class GtlTimbre
	{
		public byte Patch;
		public byte Bank;
		/// <summary>The whole timbre as the driver gets it: a little-endian size word (counting itself), then the data.</summary>
		public byte[] Data;

		/// <summary>The size word: 14 for an FM melodic or drum timbre, 194–370 for a TVFX effect, 248 for an MT-32 timbre.</summary>
		public int Size { get { return Data == null || Data.Length < 2 ? 0 : Data[0] | Data[1] << 8; } }
	}

	/// <summary>
	/// A Global Timbre Library (<c>UW.AD</c>, <c>UW.OPL</c>, <c>UW.MT</c>, also <c>SAMPLE.OPL</c>):
	/// a directory of 6-byte entries (patch, bank, a little-endian dword offset) ended by an entry
	/// whose bank is 0xFF (only its first two bytes are there), then each timbre as a size word that
	/// counts itself and the data (uw1/uw2 sound/SOUND.C load_global_timbre). The game opens
	/// <c>SOUND\UW.</c> + the music driver's data suffix: AD for the FM cards, OPL for UW2's
	/// Sound Blaster Pro 2 driver (DM07.ADV), MT for the MT-32 (SOUND.C init_timbres).
	/// Bank 0 holds the melodic programs, bank 1 the effects' timbres (SOUNDS.DAT's patches) and,
	/// in the FM files, bank 127 the drums, one timbre per key (exhume/runtime/port/sound/yamaha.c
	/// note_on: channel 10 takes timbre 127:key).
	/// </summary>
	public sealed class TimbreLibrary
	{
		/// <summary>The timbres in directory order.</summary>
		public readonly List<GtlTimbre> Timbres = new List<GtlTimbre>();
		/// <summary>The terminator's patch byte (0xFF in the shipped files).</summary>
		public byte EndPatch = 0xFF;
		/// <summary>True when the timbres follow each other in directory order right after the directory (as in every shipped file): ToBytes then rebuilds the file exactly.</summary>
		public bool Packed { get; private set; }
		byte[] raw;

		public static TimbreLibrary Parse(byte[] d)
		{
			var lib = new TimbreLibrary();
			int p = 0;
			var offsets = new List<long>();
			while (true)
			{
				if (p + 2 > d.Length) throw new FormatException("The timbre directory has no end.");
				if (d[p + 1] == 0xFF) { lib.EndPatch = d[p]; p += 2; break; }
				if (p + 6 > d.Length) throw new FormatException("The timbre directory has no end.");
				long off = d[p + 2] | (long)d[p + 3] << 8 | (long)d[p + 4] << 16 | (long)d[p + 5] << 24;
				if (off + 2 > d.Length) throw new FormatException(string.Format("Timbre {0}:{1} lies past the end of the file.", d[p + 1], d[p]));
				int size = d[off] | d[off + 1] << 8;
				if (size < 2 || off + size > d.Length) throw new FormatException(string.Format("Timbre {0}:{1} has a bad size.", d[p + 1], d[p]));
				var data = new byte[size];
				Array.Copy(d, off, data, 0, size);
				lib.Timbres.Add(new GtlTimbre { Patch = d[p], Bank = d[p + 1], Data = data });
				offsets.Add(off);
				p += 6;
			}
			long at = p;
			bool packed = true;
			for (int i = 0; i < offsets.Count; i++)
			{
				if (offsets[i] != at) { packed = false; break; }
				at += lib.Timbres[i].Data.Length;
			}
			lib.Packed = packed && at == d.Length;
			lib.raw = (byte[])d.Clone();
			return lib;
		}

		/// <summary>The library: the directory, then the timbres packed in directory order. A file read in another layout comes back as it was until a timbre is changed.</summary>
		public byte[] ToBytes()
		{
			if (!Packed && raw != null) return (byte[])raw.Clone();
			var o = new List<byte>();
			long off = Timbres.Count * 6 + 2;
			foreach (var t in Timbres)
			{
				o.Add(t.Patch); o.Add(t.Bank);
				o.Add((byte)off); o.Add((byte)(off >> 8)); o.Add((byte)(off >> 16)); o.Add((byte)(off >> 24));
				off += t.Data.Length;
			}
			o.Add(EndPatch); o.Add(0xFF);
			foreach (var t in Timbres) o.AddRange(t.Data);
			return o.ToArray();
		}

		/// <summary>Marks the library changed: ToBytes rebuilds it.</summary>
		public void Changed() { Packed = true; raw = null; }

		/// <summary>The timbre bank:patch, or null.</summary>
		public GtlTimbre Find(int bank, int patch)
		{
			foreach (var t in Timbres) if (t.Bank == bank && t.Patch == patch) return t;
			return null;
		}

		public bool Contains(int bank, int patch) { return Find(bank, patch) != null; }

		/// <summary>The patch numbers of a bank, sorted.</summary>
		public List<int> Patches(int bank)
		{
			var l = new List<int>();
			foreach (var t in Timbres) if (t.Bank == bank) l.Add(t.Patch);
			l.Sort();
			return l;
		}

		/// <summary>The banks there are, sorted.</summary>
		public List<int> Banks
		{
			get
			{
				var l = new List<int>();
				foreach (var t in Timbres) if (!l.Contains(t.Bank)) l.Add(t.Bank);
				l.Sort();
				return l;
			}
		}

		/// <summary>The drum keys: bank 127's patches.</summary>
		public List<int> DrumKeys { get { return Patches(127); } }

		/// <summary>True when the timbres are MT-32 ones (248 bytes each).</summary>
		public bool IsMt32 { get { return Timbres.Count > 0 && Timbres.TrueForAll(t => t.Size == 248); } }
	}
}
