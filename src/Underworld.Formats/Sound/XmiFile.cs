using System;
using System.Collections.Generic;
using System.Text;

namespace UltimateEditor.Sound
{
	/// <summary>
	/// A chunk of an IFF file as XMIDI uses it: a 4-character id and a big-endian length, then
	/// either data or, for FORM and CAT, a 4-character type and the chunks inside. A chunk of
	/// odd length is followed by one pad byte, kept as it was read.
	/// </summary>
	public sealed class XmiChunk
	{
		/// <summary>"FORM", "CAT ", "INFO", "TIMB", "RBRN", "EVNT"...</summary>
		public string Id;
		/// <summary>FORM and CAT: the type ("XDIR", "XMID"); null for a data chunk.</summary>
		public string Type;
		/// <summary>A data chunk's bytes (null for FORM and CAT).</summary>
		public byte[] Data;
		public readonly List<XmiChunk> Children = new List<XmiChunk>();
		/// <summary>The pad byte after a chunk of odd length (0 normally).</summary>
		public byte Pad;
		/// <summary>A chunk of odd length that had no pad byte after it (some tools leave it out).</summary>
		public bool PadMissing;

		public bool IsGroup { get { return Id == "FORM" || Id == "CAT " || Id == "LIST"; } }

		/// <summary>The first child chunk with this id (and type, for FORM/CAT), or null.</summary>
		public XmiChunk Find(string id, string type = null)
		{
			foreach (var c in Children)
				if (c.Id == id && (type == null || c.Type == type)) return c;
			return null;
		}

		/// <summary>The length field the chunk is written with: its data, or the type and the chunks inside.</summary>
		public long ContentLength()
		{
			if (Id == null) return Data.Length;
			if (!IsGroup) return Data.Length;
			long n = 4;
			foreach (var c in Children) n += c.TotalLength();
			return n;
		}

		/// <summary>Header, body and pad (raw bytes: just their length).</summary>
		public long TotalLength()
		{
			if (Id == null) return Data.Length;
			long n = ContentLength();
			return 8 + n + ((n & 1) != 0 && !PadMissing ? 1 : 0);
		}
	}

	/// <summary>
	/// An Extended MIDI file (.XMI), the music of both games: <c>FORM XDIR</c> (an INFO chunk:
	/// the number of sequences, a little-endian word), then <c>CAT XMID</c> holding one
	/// <c>FORM XMID</c> per sequence with <c>TIMB</c> (the timbres the sequence needs: a word
	/// count, then patch and bank bytes), optionally <c>RBRN</c> (branch points: a word count,
	/// then 6-byte entries, the controller 120 value as a word and the event's byte offset in
	/// EVNT as a dword) and <c>EVNT</c> (the events, <see cref="XmiEvents"/>). The games load
	/// sequence 0 (uw1/uw2 sound/SOUND.C load_new_music: AIL_register_sequence(…, 0, …); AIL's
	/// XMIDI.ASM find_seq, exhume/runtime/port/sound/xmidi.c). Parse keeps every chunk and byte,
	/// so ToBytes gives the file back unchanged.
	/// </summary>
	public sealed class XmiFile
	{
		/// <summary>The top-level chunks (normally FORM XDIR and CAT XMID).</summary>
		public readonly List<XmiChunk> Chunks = new List<XmiChunk>();
		/// <summary>Bytes after the last top-level chunk, kept.</summary>
		public byte[] Trailer = new byte[0];

		public static XmiFile Parse(byte[] d)
		{
			if (d == null || d.Length < 12) throw new FormatException("Too short for an XMI file.");
			var f = new XmiFile();
			int p = ReadChunks(d, 0, d.Length, f.Chunks);
			f.Trailer = new byte[d.Length - p];
			Array.Copy(d, p, f.Trailer, 0, f.Trailer.Length);
			if (f.Sequences.Count == 0) throw new FormatException("No FORM XMID in the file.");
			return f;
		}

		/// <summary>Reads chunks from p to end; returns where it stopped (at the first chunk that does not fit).</summary>
		static int ReadChunks(byte[] d, int p, int end, List<XmiChunk> into)
		{
			while (p + 8 <= end)
			{
				string id = Encoding.ASCII.GetString(d, p, 4);
				if (!IsId(id)) break;
				long len = (long)d[p + 4] << 24 | (long)d[p + 5] << 16 | (long)d[p + 6] << 8 | d[p + 7];
				if (p + 8 + len > end) break;
				var c = new XmiChunk { Id = id };
				if (c.IsGroup)
				{
					if (len < 4) break;
					c.Type = Encoding.ASCII.GetString(d, p + 8, 4);
					int inner = ReadChunks(d, p + 12, (int)(p + 8 + len), c.Children);
					if (inner != p + 8 + len)
					{
						// bytes inside the group that are not whole chunks: keep them as a raw child
						var raw = new byte[p + 8 + len - inner];
						Array.Copy(d, inner, raw, 0, raw.Length);
						c.Children.Add(new XmiChunk { Id = null, Data = raw });
					}
				}
				else
				{
					c.Data = new byte[len];
					Array.Copy(d, p + 8, c.Data, 0, (int)len);
				}
				p += 8 + (int)len;
				if ((len & 1) != 0)
				{
					if (p < end && d[p] == 0) p++;
					else c.PadMissing = true;
				}
				into.Add(c);
			}
			return p;
		}

		static bool IsId(string id)
		{
			foreach (char ch in id) if (ch < 0x20 || ch > 0x7E) return false;
			return true;
		}

		public byte[] ToBytes()
		{
			var o = new List<byte>();
			foreach (var c in Chunks) Write(c, o);
			o.AddRange(Trailer);
			return o.ToArray();
		}

		static void Write(XmiChunk c, List<byte> o)
		{
			if (c.Id == null) { o.AddRange(c.Data); return; }        // raw bytes inside a group
			o.AddRange(Encoding.ASCII.GetBytes(c.Id));
			long len = c.ContentLength();
			o.Add((byte)(len >> 24)); o.Add((byte)(len >> 16)); o.Add((byte)(len >> 8)); o.Add((byte)len);
			int start = o.Count;
			if (c.IsGroup)
			{
				o.AddRange(Encoding.ASCII.GetBytes(c.Type));
				foreach (var k in c.Children) Write(k, o);
			}
			else o.AddRange(c.Data);
			if (((o.Count - start) & 1) != 0 && !c.PadMissing) o.Add(c.Pad);
		}

		/// <summary>Every FORM XMID, in order (inside CAT XMID, or at the top level).</summary>
		public List<XmiChunk> Sequences
		{
			get
			{
				var l = new List<XmiChunk>();
				foreach (var c in Chunks)
				{
					if (c.Id == "FORM" && c.Type == "XMID") l.Add(c);
					if (c.Id == "CAT " && c.Type == "XMID")
						foreach (var k in c.Children) if (k.Id == "FORM" && k.Type == "XMID") l.Add(k);
				}
				return l;
			}
		}

		/// <summary>The sequence count INFO gives (-1 when there is no INFO chunk).</summary>
		public int InfoCount
		{
			get
			{
				var dir = Chunks.Find(c => c.Id == "FORM" && c.Type == "XDIR");
				var info = dir == null ? null : dir.Find("INFO");
				if (info == null || info.Data.Length < 2) return -1;
				return info.Data[0] | info.Data[1] << 8;
			}
		}

		/// <summary>Sequence 0, the one the games play.</summary>
		public XmiChunk Sequence { get { var s = Sequences; return s.Count > 0 ? s[0] : null; } }

		/// <summary>Sequence 0's TIMB list: (patch, bank) pairs.</summary>
		public List<XmiTimbre> Timbres
		{
			get
			{
				var l = new List<XmiTimbre>();
				var t = Sequence == null ? null : Sequence.Find("TIMB");
				if (t == null || t.Data.Length < 2) return l;
				int n = t.Data[0] | t.Data[1] << 8;
				for (int i = 0; i < n && 2 + 2 * i + 1 < t.Data.Length; i++) l.Add(new XmiTimbre(t.Data[2 + 2 * i], t.Data[3 + 2 * i]));
				return l;
			}
		}

		/// <summary>Sequence 0's EVNT bytes (null without one).</summary>
		public byte[] EventData { get { var e = Sequence == null ? null : Sequence.Find("EVNT"); return e == null ? null : e.Data; } }

		/// <summary>Sequence 0's events.</summary>
		public List<XmiEvent> Events() { return XmiEvents.Parse(EventData); }

		/// <summary>
		/// A new one-sequence file in the shape MIDIFORM gives the games' themes: FORM XDIR with
		/// INFO 1, CAT XMID, FORM XMID with TIMB, RBRN when a controller 120 is there (branch
		/// points at its byte offsets, as every UW2 theme has) and EVNT.
		/// </summary>
		public static XmiFile Build(IList<XmiTimbre> timbres, IList<XmiEvent> events)
		{
			byte[] evnt = XmiEvents.Encode(events, out List<KeyValuePair<int, int>> branches);
			var timb = new byte[2 + 2 * timbres.Count];
			timb[0] = (byte)timbres.Count; timb[1] = (byte)(timbres.Count >> 8);
			for (int i = 0; i < timbres.Count; i++) { timb[2 + 2 * i] = timbres[i].Patch; timb[3 + 2 * i] = timbres[i].Bank; }
			var seq = new XmiChunk { Id = "FORM", Type = "XMID" };
			seq.Children.Add(new XmiChunk { Id = "TIMB", Data = timb });
			if (branches.Count > 0)
			{
				var r = new byte[2 + 6 * branches.Count];
				r[0] = (byte)branches.Count; r[1] = (byte)(branches.Count >> 8);
				for (int i = 0; i < branches.Count; i++)
				{
					int at = 2 + 6 * i, marker = branches[i].Key, off = branches[i].Value;
					r[at] = (byte)marker; r[at + 1] = (byte)(marker >> 8);
					r[at + 2] = (byte)off; r[at + 3] = (byte)(off >> 8); r[at + 4] = (byte)(off >> 16); r[at + 5] = (byte)(off >> 24);
				}
				seq.Children.Add(new XmiChunk { Id = "RBRN", Data = r });
			}
			seq.Children.Add(new XmiChunk { Id = "EVNT", Data = evnt });
			var dir = new XmiChunk { Id = "FORM", Type = "XDIR" };
			dir.Children.Add(new XmiChunk { Id = "INFO", Data = new byte[] { 1, 0 } });
			var cat = new XmiChunk { Id = "CAT ", Type = "XMID" };
			cat.Children.Add(seq);
			var f = new XmiFile();
			f.Chunks.Add(dir);
			f.Chunks.Add(cat);
			return f;
		}
	}

	/// <summary>A TIMB entry: a timbre the sequence needs, by patch (program) and bank.</summary>
	public struct XmiTimbre : IEquatable<XmiTimbre>
	{
		public readonly byte Patch;
		public readonly byte Bank;
		public XmiTimbre(int patch, int bank) { Patch = (byte)patch; Bank = (byte)bank; }
		public bool Equals(XmiTimbre o) { return Patch == o.Patch && Bank == o.Bank; }
		public override bool Equals(object o) { return o is XmiTimbre && Equals((XmiTimbre)o); }
		public override int GetHashCode() { return Bank << 8 | Patch; }
		public override string ToString() { return Bank + ":" + Patch; }
	}

	/// <summary>
	/// An XMIDI event at its time in 120 Hz ticks. A note carries its duration (there are no
	/// note-offs); controllers, programs, pitch bends and pressure are as in MIDI; meta events
	/// (0xFF) and system exclusive messages (0xF0, 0xF7) keep their bytes.
	/// </summary>
	public sealed class XmiEvent
	{
		/// <summary>The time, in ticks of 1/120 s from the start.</summary>
		public int Tick { get; set; }
		/// <summary>The status byte: 0x90 | channel for a note, 0xB0 | channel for a controller... 0xFF meta, 0xF0/0xF7 system exclusive.</summary>
		public byte Status { get; set; }
		/// <summary>A note's key, a controller's number, a program; a meta event's type is <see cref="MetaType"/>.</summary>
		public byte Data1 { get; set; }
		/// <summary>A note's velocity, a controller's value, a pitch bend's high byte.</summary>
		public byte Data2 { get; set; }
		/// <summary>A note's duration in ticks.</summary>
		public int Duration { get; set; }
		/// <summary>A meta event's type (0x2F end of track, 0x51 tempo, 0x58 time signature...).</summary>
		public byte MetaType { get; set; }
		/// <summary>A meta event's or system exclusive message's data.</summary>
		public byte[] Data { get; set; }

		public int Channel { get { return Status & 0x0F; } }
		public int Kind { get { return Status & 0xF0; } }
		public bool IsNote { get { return Kind == 0x90; } }
		public bool IsController { get { return Kind == 0xB0; } }
		public bool IsProgram { get { return Kind == 0xC0; } }
		public bool IsMeta { get { return Status == 0xFF; } }
		public bool IsSysEx { get { return Status == 0xF0 || Status == 0xF7; } }
		public bool IsEndOfTrack { get { return IsMeta && MetaType == 0x2F; } }

		public XmiEvent Clone()
		{
			return new XmiEvent { Tick = Tick, Status = Status, Data1 = Data1, Data2 = Data2, Duration = Duration, MetaType = MetaType, Data = Data == null ? null : (byte[])Data.Clone() };
		}

		/// <summary>A text form with everything that makes the event (for comparing and reports).</summary>
		public override string ToString()
		{
			string data = Data == null ? "" : " " + BitConverter.ToString(Data);
			if (IsNote) return string.Format("{0} note ch{1} key {2} vel {3} dur {4}", Tick, Channel + 1, Data1, Data2, Duration);
			if (IsMeta) return string.Format("{0} meta {1:X2}{2}", Tick, MetaType, data);
			if (IsSysEx) return string.Format("{0} sysex {1:X2}{2}", Tick, Status, data);
			if (Kind == 0xC0 || Kind == 0xD0) return string.Format("{0} {1:X2} {2}", Tick, Status, Data1);
			return string.Format("{0} {1:X2} {2} {3}", Tick, Status, Data1, Data2);
		}
	}

	/// <summary>
	/// The EVNT chunk's event stream, as AIL's XMIDI sequencer reads it (XMIDI.ASM serve_driver;
	/// exhume/runtime/port/sound/xmidi.c xmidi_serve): a byte below 0x80 is an interval, that many
	/// ticks of 1/120 s before the next event (several in a row add up; MIDIFORM writes 0x7F bytes
	/// and the rest); a note-on is status, key, velocity and the duration as a MIDI variable-length
	/// number; meta events are 0xFF, type, a variable length and data; system exclusive is 0xF0 or
	/// 0xF7, a variable length and data; other channel messages are as in MIDI (no running status).
	/// The stream ends at the end-of-track meta event; MIDIFORM pads the chunk to an even length.
	/// </summary>
	public static class XmiEvents
	{
		public static List<XmiEvent> Parse(byte[] d)
		{
			var l = new List<XmiEvent>();
			if (d == null) return l;
			int p = 0, tick = 0;
			while (p < d.Length)
			{
				byte s = d[p];
				if (s < 0x80) { tick += s; p++; continue; }
				var e = new XmiEvent { Tick = tick, Status = s };
				if (s == 0xFF)
				{
					if (p + 2 > d.Length) break;
					e.MetaType = d[p + 1];
					int q = p + 2;
					int len = (int)ReadVlq(d, ref q);
					if (q + len > d.Length) throw new FormatException("A meta event runs past the end of EVNT.");
					e.Data = Slice(d, q, len);
					p = q + len;
					l.Add(e);
					if (e.MetaType == 0x2F) break;
					continue;
				}
				if (s == 0xF0 || s == 0xF7)
				{
					int q = p + 1;
					int len = (int)ReadVlq(d, ref q);
					if (q + len > d.Length) throw new FormatException("A system exclusive message runs past the end of EVNT.");
					e.Data = Slice(d, q, len);
					p = q + len;
					l.Add(e);
					continue;
				}
				int kind = s & 0xF0;
				if (s >= 0xF1) throw new FormatException(string.Format("Unknown status {0:X2} at byte {1} of EVNT.", s, p));
				int size = kind == 0xC0 || kind == 0xD0 ? 2 : 3;
				if (p + size > d.Length) throw new FormatException("An event runs past the end of EVNT.");
				e.Data1 = d[p + 1];
				if (size == 3) e.Data2 = d[p + 2];
				p += size;
				if (kind == 0x90) e.Duration = (int)ReadVlq(d, ref p);
				l.Add(e);
			}
			return l;
		}

		/// <summary>Encodes events (in time order) as an EVNT chunk's data, padded to an even length.</summary>
		public static byte[] Encode(IList<XmiEvent> events)
		{
			return Encode(events, out List<KeyValuePair<int, int>> _);
		}

		/// <summary>As Encode; branches gets each controller 120's value and byte offset (for RBRN).</summary>
		public static byte[] Encode(IList<XmiEvent> events, out List<KeyValuePair<int, int>> branches)
		{
			var o = new List<byte>();
			branches = new List<KeyValuePair<int, int>>();
			int tick = 0;
			bool ended = false;
			foreach (var e in events)
			{
				if (e.Tick < tick) throw new ArgumentException("Events out of time order.");
				int gap = e.Tick - tick;
				while (gap > 0x7F) { o.Add(0x7F); gap -= 0x7F; }
				if (gap > 0) o.Add((byte)gap);
				tick = e.Tick;
				if (e.IsController && e.Data1 == 120) branches.Add(new KeyValuePair<int, int>(e.Data2, o.Count));
				o.Add(e.Status);
				if (e.IsMeta)
				{
					o.Add(e.MetaType);
					var data = e.Data ?? new byte[0];
					WriteVlq(o, data.Length);
					o.AddRange(data);
					if (e.MetaType == 0x2F) { ended = true; break; }
					continue;
				}
				if (e.IsSysEx)
				{
					var data = e.Data ?? new byte[0];
					WriteVlq(o, data.Length);
					o.AddRange(data);
					continue;
				}
				o.Add((byte)(e.Data1 & 0x7F));
				if (e.Kind != 0xC0 && e.Kind != 0xD0) o.Add((byte)(e.Data2 & 0x7F));
				if (e.IsNote) WriteVlq(o, Math.Max(0, e.Duration));
			}
			if (!ended) { o.Add(0xFF); o.Add(0x2F); o.Add(0); }
			if ((o.Count & 1) != 0) o.Add(0);
			return o.ToArray();
		}

		public static long ReadVlq(byte[] d, ref int p)
		{
			long v = 0;
			for (int i = 0; i < 5 && p < d.Length; i++)
			{
				byte c = d[p++];
				v = v << 7 | (byte)(c & 0x7F);
				if ((c & 0x80) == 0) return v;
			}
			return v;
		}

		public static void WriteVlq(List<byte> o, long v)
		{
			var tmp = new Stack<byte>();
			tmp.Push((byte)(v & 0x7F));
			while ((v >>= 7) > 0) tmp.Push((byte)(0x80 | (v & 0x7F)));
			o.AddRange(tmp);
		}

		static byte[] Slice(byte[] d, int at, int n)
		{
			var r = new byte[n];
			Array.Copy(d, at, r, 0, n);
			return r;
		}
	}
}
