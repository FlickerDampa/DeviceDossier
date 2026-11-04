using System;
using System.Collections.Generic;
using System.Linq;

namespace DeviceDossier.Core
{
    public enum CaptionStyle { None, Bold, Italic }

    public enum BlockKind { KeyValue, Table }

    public enum SectionState { Ok, Warning, Error, Skipped }

    public sealed class Field
    {
        public Field(string name, object value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; private set; }
        public object Value { get; private set; }
        public string Text { get { return Fmt.Text(Value); } }
    }

    public sealed class Block
    {
        private Block(BlockKind kind)
        {
            Kind = kind;
            Fields = new List<Field>();
            Rows = new List<object[]>();
            Headers = new string[0];
        }

        public BlockKind Kind { get; private set; }
        public string Caption { get; set; }
        public CaptionStyle CaptionStyle { get; set; }
        public List<Field> Fields { get; private set; }
        public string[] Headers { get; private set; }
        public List<object[]> Rows { get; private set; }

        public static Block KeyValue()
        {
            return new Block(BlockKind.KeyValue);
        }

        public static Block Table(params string[] headers)
        {
            var b = new Block(BlockKind.Table);
            b.Headers = headers;
            return b;
        }

        public Block WithCaption(string caption, CaptionStyle style)
        {
            Caption = caption;
            CaptionStyle = style;
            return this;
        }

        public Block Add(string name, object value)
        {
            Fields.Add(new Field(name, value));
            return this;
        }

        public Block AddRow(params object[] values)
        {
            var row = new object[Headers.Length];
            for (int i = 0; i < row.Length; i++) row[i] = i < values.Length ? values[i] : null;
            Rows.Add(row);
            return this;
        }

        public string Get(string name)
        {
            var f = Fields.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            return f == null ? null : f.Text;
        }

        public int Column(string header)
        {
            return Array.FindIndex(Headers, h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class Section
    {
        public Section(string id, string number, string title)
        {
            Id = id;
            Number = number;
            Title = title;
            Blocks = new List<Block>();
            State = SectionState.Ok;
        }

        public string Id { get; private set; }
        public string Number { get; private set; }
        public string Title { get; set; }
        public List<Block> Blocks { get; private set; }
        public SectionState State { get; set; }
        public string StateNote { get; set; }

        public string Heading { get { return Number + ". " + Title; } }

        public Block First { get { return Blocks.Count > 0 ? Blocks[0] : null; } }

        public Section With(Block block)
        {
            Blocks.Add(block);
            return this;
        }

        public void Warn(string note)
        {
            if (State == SectionState.Ok) State = SectionState.Warning;
            StateNote = string.IsNullOrEmpty(StateNote) ? note : StateNote + " " + note;
        }
    }

    public sealed class AuditReport
    {
        public AuditReport()
        {
            Sections = new List<Section>();
            Generated = DateTime.Now;
            ToolName = AppInfo.Name;
        }

        public string ToolName { get; set; }

        public string DeviceName { get; set; }
        public DateTime Generated { get; set; }
        public bool IsLaptop { get; set; }
        public bool RanAsAdmin { get; set; }
        public string RunAsUser { get; set; }
        public string ToolVersion { get; set; }
        public string SavedTo { get; set; }
        public List<Section> Sections { get; private set; }

        public Section this[string id]
        {
            get { return Sections.FirstOrDefault(s => s.Id == id); }
        }

        public void Add(Section section)
        {
            Sections.Add(section);
        }
    }
}
