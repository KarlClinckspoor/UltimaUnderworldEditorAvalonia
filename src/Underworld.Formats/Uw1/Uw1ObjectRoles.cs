namespace UltimateEditor.Uw1
{
	/// <summary>A field's meaning for an item (from the Unity editor's Uw1ObjectRoles; only the role type is needed here).</summary>
	public static class Uw1ObjectRoles
	{
		public struct Role
		{
			public readonly string Field;
			public readonly string Meaning;
			public readonly string Source;
			public Role(string field, string meaning, string source) { Field = field; Meaning = meaning; Source = source; }
			public override string ToString() { return Field + ": " + Meaning; }
		}
	}
}
