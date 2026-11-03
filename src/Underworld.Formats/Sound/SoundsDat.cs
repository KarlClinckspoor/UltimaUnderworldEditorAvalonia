using System;
using System.Collections.Generic;
using System.IO;

namespace UltimateEditor.Sound
{
	/// <summary>
	/// One effect of SOUNDS.DAT: patch (a timbre in bank 1), note, volume, length (stored high
	/// byte first), and in UW2 a digital flag and a priority (byte 6 + byte 7 × 200). The bytes
	/// are kept as read; the properties read and write them.
	/// </summary>
	public sealed class SoundEffect
	{
		public readonly byte[] Bytes;
		public SoundEffect(byte[] bytes) { Bytes = bytes; }
		public bool IsUw2 { get { return Bytes.Length >= 8; } }

		public int Patch { get { return Bytes[0]; } set { Bytes[0] = Byte(value); } }
		public int Note { get { return Bytes[1]; } set { Bytes[1] = Byte(value); } }
		public int Volume { get { return Bytes[2]; } set { Bytes[2] = Byte(value); } }
		/// <summary>The length, high byte first (uw1/src/sound/SOUND.C read_fx_data, uw2 SOUND.C:1308).</summary>
		public int Length
		{
			get { return Bytes[3] << 8 | Bytes[4]; }
			set { if (value < 0 || value > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(value)); Bytes[3] = (byte)(value >> 8); Bytes[4] = (byte)value; }
		}
		/// <summary>UW2: nonzero plays SOUND\SPnn.VOC (nn the effect number) on the digital card.</summary>
		public bool Digital
		{
			get { return IsUw2 && Bytes[5] != 0; }
			set { if (!IsUw2) throw new InvalidOperationException("Ultima Underworld's effects are MIDI only."); if (value != Digital) Bytes[5] = (byte)(value ? 1 : 0); }
		}
		/// <summary>UW2: byte 6 + byte 7 × 200 (uw2/src/sound/SOUND.C:1310). Setting it writes byte 6 below 200.</summary>
		public int Priority
		{
			get { return IsUw2 ? Bytes[6] + Bytes[7] * 200 : 0; }
			set
			{
				if (!IsUw2) throw new InvalidOperationException("Ultima Underworld's effects have no priority.");
				if (value < 0 || value > 255 + 255 * 200) throw new ArgumentOutOfRangeException(nameof(value));
				if (value == Priority) return;
				int hi = Math.Min(255, value / 200);
				Bytes[7] = (byte)hi; Bytes[6] = (byte)(value - hi * 200);
			}
		}

		static byte Byte(int v) { if (v < 0 || v > 255) throw new ArgumentOutOfRangeException(nameof(v)); return (byte)v; }
	}

	/// <summary>
	/// SOUND\SOUNDS.DAT: a count byte, then 5 bytes an effect (UW1) or 8 (UW2). The games' tables
	/// hold 24 (UW1) and 49 (UW2) effects and the count is not checked (docs/FORMATS.md "Sound and
	/// music"); the shipped files fill them. Bytes after the effects are kept.
	/// </summary>
	public sealed class SoundsDat
	{
		public const int Uw1Max = 24, Uw2Max = 49;
		public bool IsUw2 { get; private set; }
		public readonly List<SoundEffect> Effects = new List<SoundEffect>();
		public byte[] Trailing { get; private set; } = new byte[0];
		public int EntrySize { get { return IsUw2 ? 8 : 5; } }
		public int MaxEffects { get { return IsUw2 ? Uw2Max : Uw1Max; } }

		public static SoundsDat Parse(byte[] d, bool uw2)
		{
			if (d == null || d.Length < 1) throw new InvalidDataException("SOUNDS.DAT is empty.");
			var s = new SoundsDat { IsUw2 = uw2 };
			int n = d[0], size = s.EntrySize;
			if (1 + n * size > d.Length) throw new InvalidDataException(string.Format("SOUNDS.DAT says {0} effects of {1} bytes but has {2} bytes: is it the other game's?", n, size, d.Length));
			for (int i = 0; i < n; i++)
			{
				var b = new byte[size];
				Array.Copy(d, 1 + i * size, b, 0, size);
				s.Effects.Add(new SoundEffect(b));
			}
			int end = 1 + n * size;
			s.Trailing = new byte[d.Length - end];
			Array.Copy(d, end, s.Trailing, 0, s.Trailing.Length);
			return s;
		}

		public byte[] ToBytes()
		{
			if (Effects.Count > 255) throw new InvalidOperationException("Too many effects.");
			var ms = new MemoryStream();
			ms.WriteByte((byte)Effects.Count);
			foreach (var e in Effects) ms.Write(e.Bytes, 0, e.Bytes.Length);
			ms.Write(Trailing, 0, Trailing.Length);
			return ms.ToArray();
		}

		public SoundsDat Clone() { return Parse(ToBytes(), IsUw2); }
	}
}
