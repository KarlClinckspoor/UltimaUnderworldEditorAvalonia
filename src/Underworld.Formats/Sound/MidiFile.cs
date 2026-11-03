using System;
using System.Collections.Generic;
using System.Text;

namespace UltimateEditor.Sound
{
	/// <summary>An event of a standard MIDI file track, at its absolute time in the file's ticks.</summary>
	public sealed class MidiEvent
	{
		public long Tick { get; set; }
		/// <summary>0x80–0xEF a channel message; 0xFF meta; 0xF0 or 0xF7 system exclusive.</summary>
		public byte Status { get; set; }
		public byte Data1 { get; set; }
		public byte Data2 { get; set; }
		/// <summary>A meta event's type.</summary>
		public byte MetaType { get; set; }
		/// <summary>A meta event's or system exclusive message's data (for 0xF0, without the 0xF0 itself).</summary>
		public byte[] Data { get; set; }

		public int Channel { get { return Status & 0x0F; } }
		public int Kind { get { return Status & 0xF0; } }
		public bool IsMeta { get { return Status == 0xFF; } }
		public bool IsSysEx { get { return Status == 0xF0 || Status == 0xF7; } }
		public bool IsNoteOn { get { return Kind == 0x90 && Data2 > 0; } }
		/// <summary>A note-off, or a note-on of velocity 0.</summary>
		public bool IsNoteOff { get { return Kind == 0x80 || Kind == 0x90 && Data2 == 0; } }

		/// <summary>A meta event's data as text (Latin-1).</summary>
		public string Text { get { return Data == null ? "" : Encoding.GetEncoding("ISO-8859-1").GetString(Data); } }

		public static MidiEvent Meta(long tick, int type, byte[] data) { return new MidiEvent { Tick = tick, Status = 0xFF, MetaType = (byte)type, Data = data ?? new byte[0] }; }
		public static MidiEvent Channel3(long tick, int status, int d1, int d2) { return new MidiEvent { Tick = tick, Status = (byte)status, Data1 = (byte)d1, Data2 = (byte)d2 }; }
		public static MidiEvent TextMeta(long tick, int type, string text) { return Meta(tick, type, Encoding.GetEncoding("ISO-8859-1").GetBytes(text)); }
	}

	public sealed class MidiTrack
	{
		/// <summary>The events in file order (their ticks never go down).</summary>
		public readonly List<MidiEvent> Events = new List<MidiEvent>();
	}

	/// <summary>
	/// A standard MIDI file (format 0, 1 or 2): the MThd header (format, track count, division)
	/// and its MTrk chunks. Reading accepts running status, velocity-0 note-offs, unknown chunks
	/// (skipped), a RIFF RMID wrapper and a last track cut short; times are made absolute.
	/// Writing gives one MTrk per track, without running status, each ended by an end-of-track
	/// event.
	/// </summary>
	public sealed class MidiFile
	{
		public int Format { get; set; }
		/// <summary>Ticks per quarter note; or, when the top bit is set, SMPTE: the high byte is minus the frames a second and the low byte the ticks a frame.</summary>
		public int Division { get; set; } = 60;
		public readonly List<MidiTrack> Tracks = new List<MidiTrack>();

		public bool IsSmpte { get { return (Division & 0x8000) != 0; } }
		/// <summary>SMPTE: ticks a second.</summary>
		public int SmpteTicksPerSecond { get { int fps = -(sbyte)(Division >> 8); if (fps == 29) fps = 30; return fps * (Division & 0xFF); } }

		public static bool LooksLikeMidi(byte[] d)
		{
			return d != null && d.Length >= 14 && (Tag(d, 0) == "MThd" || Tag(d, 0) == "RIFF" && Tag(d, 8) == "RMID");
		}

		public static MidiFile Parse(byte[] d)
		{
			if (d == null || d.Length < 14) throw new FormatException("Too short for a MIDI file.");
			int p = 0;
			if (Tag(d, 0) == "RIFF" && d.Length >= 20 && Tag(d, 8) == "RMID")
			{
				p = 12;
				while (p + 8 <= d.Length && Tag(d, p) != "data") p += 8 + (int)Le32(d, p + 4) + (int)(Le32(d, p + 4) & 1);
				if (p + 8 > d.Length) throw new FormatException("An RMID file without its data chunk.");
				p += 8;
			}
			if (Tag(d, p) != "MThd") throw new FormatException("Not a standard MIDI file (no MThd).");
			int hlen = (int)Be32(d, p + 4);
			var f = new MidiFile
			{
				Format = d[p + 8] << 8 | d[p + 9],
				Division = d[p + 12] << 8 | d[p + 13],
			};
			int ntracks = d[p + 10] << 8 | d[p + 11];
			if (f.Format > 2) throw new FormatException("MIDI format " + f.Format + " is not one of 0, 1, 2.");
			if (!f.IsSmpte && f.Division == 0) throw new FormatException("A MIDI file with 0 ticks a beat.");
			p += 8 + hlen;
			while (p + 8 <= d.Length && f.Tracks.Count < ntracks)
			{
				string tag = Tag(d, p);
				long len = Be32(d, p + 4);
				int start = p + 8;
				int end = (int)Math.Min(d.Length, start + len);
				if (tag == "MTrk") f.Tracks.Add(ReadTrack(d, start, end));
				p = (int)Math.Min(d.Length, start + len);
			}
			if (f.Tracks.Count == 0) throw new FormatException("The MIDI file has no tracks.");
			return f;
		}

		static MidiTrack ReadTrack(byte[] d, int p, int end)
		{
			var t = new MidiTrack();
			long tick = 0;
			byte running = 0;
			while (p < end)
			{
				long delta = ReadVlq(d, ref p, end);
				tick += delta;
				if (p >= end) break;
				byte s = d[p];
				if (s == 0xFF)
				{
					if (p + 2 > end) break;
					byte type = d[p + 1];
					p += 2;
					int len = (int)ReadVlq(d, ref p, end);
					if (p + len > end) len = end - p;
					t.Events.Add(MidiEvent.Meta(tick, type, Slice(d, p, len)));
					p += len;
					running = 0;
					if (type == 0x2F) break;
					continue;
				}
				if (s == 0xF0 || s == 0xF7)
				{
					p++;
					int len = (int)ReadVlq(d, ref p, end);
					if (p + len > end) len = end - p;
					t.Events.Add(new MidiEvent { Tick = tick, Status = s, Data = Slice(d, p, len) });
					p += len;
					running = 0;
					continue;
				}
				if (s >= 0x80) { running = s; p++; }
				else if (running == 0) throw new FormatException("A data byte without a status (running status lost).");
				if (running >= 0xF0)
				{
					// system common or real-time messages have no place in a file: skip their data
					running = 0;
					continue;
				}
				int kind = running & 0xF0;
				int n = kind == 0xC0 || kind == 0xD0 ? 1 : 2;
				if (p + n > end) break;
				var e = new MidiEvent { Tick = tick, Status = running, Data1 = (byte)(d[p] & 0x7F) };
				if (n == 2) e.Data2 = (byte)(d[p + 1] & 0x7F);
				p += n;
				t.Events.Add(e);
			}
			return t;
		}

		public byte[] ToBytes()
		{
			var o = new List<byte>();
			o.AddRange(Encoding.ASCII.GetBytes("MThd"));
			Be32(o, 6);
			o.Add((byte)(Format >> 8)); o.Add((byte)Format);
			o.Add((byte)(Tracks.Count >> 8)); o.Add((byte)Tracks.Count);
			o.Add((byte)(Division >> 8)); o.Add((byte)Division);
			foreach (var t in Tracks)
			{
				var b = new List<byte>();
				long tick = 0;
				bool ended = false;
				foreach (var e in t.Events)
				{
					long at = Math.Max(tick, e.Tick);
					WriteVlq(b, at - tick);
					tick = at;
					b.Add(e.Status);
					if (e.IsMeta)
					{
						b.Add(e.MetaType);
						var data = e.Data ?? new byte[0];
						WriteVlq(b, data.Length);
						b.AddRange(data);
						if (e.MetaType == 0x2F) { ended = true; break; }
					}
					else if (e.IsSysEx)
					{
						var data = e.Data ?? new byte[0];
						WriteVlq(b, data.Length);
						b.AddRange(data);
					}
					else
					{
						b.Add((byte)(e.Data1 & 0x7F));
						if (e.Kind != 0xC0 && e.Kind != 0xD0) b.Add((byte)(e.Data2 & 0x7F));
					}
				}
				if (!ended) { b.Add(0); b.Add(0xFF); b.Add(0x2F); b.Add(0); }
				o.AddRange(Encoding.ASCII.GetBytes("MTrk"));
				Be32(o, b.Count);
				o.AddRange(b);
			}
			return o.ToArray();
		}

		static long ReadVlq(byte[] d, ref int p, int end)
		{
			long v = 0;
			for (int i = 0; i < 5 && p < end; i++)
			{
				byte c = d[p++];
				v = v << 7 | (byte)(c & 0x7F);
				if ((c & 0x80) == 0) break;
			}
			return v;
		}

		static void WriteVlq(List<byte> o, long v)
		{
			var tmp = new Stack<byte>();
			tmp.Push((byte)(v & 0x7F));
			while ((v >>= 7) > 0) tmp.Push((byte)(0x80 | (v & 0x7F)));
			o.AddRange(tmp);
		}

		static string Tag(byte[] d, int p) { return p + 4 <= d.Length ? Encoding.ASCII.GetString(d, p, 4) : ""; }
		static long Be32(byte[] d, int p) { return (long)d[p] << 24 | (long)d[p + 1] << 16 | (long)d[p + 2] << 8 | d[p + 3]; }
		static long Le32(byte[] d, int p) { return d[p] | (long)d[p + 1] << 8 | (long)d[p + 2] << 16 | (long)d[p + 3] << 24; }
		static void Be32(List<byte> o, long v) { o.Add((byte)(v >> 24)); o.Add((byte)(v >> 16)); o.Add((byte)(v >> 8)); o.Add((byte)v); }

		static byte[] Slice(byte[] d, int at, int n)
		{
			if (n < 0) n = 0;
			var r = new byte[n];
			Array.Copy(d, at, r, 0, n);
			return r;
		}
	}
}
