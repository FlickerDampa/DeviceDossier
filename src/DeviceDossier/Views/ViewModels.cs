using System.ComponentModel;

namespace DeviceDossier.Views
{
    public sealed class SpecCard
    {
        public SpecCard(string tag, string label, string value, string detail)
        {
            Tag = tag;
            Label = label;
            Value = string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
            Detail = detail ?? "";
        }

        public string Tag { get; private set; }
        public string Label { get; private set; }
        public string Value { get; private set; }
        public string Detail { get; private set; }
    }

    public enum StepState { Pending, Running, Done, Warning, Failed, Skipped }

    public sealed class StepItem : INotifyPropertyChanged
    {
        private StepState _state;
        private string _status = "";

        public StepItem(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; private set; }
        public string Label { get; private set; }

        public StepState State
        {
            get { return _state; }
            set
            {
                _state = value;
                Raise("State");
                Raise("StateName");
                Raise("Icon");
            }
        }

        public string StateName
        {
            get { return _state.ToString(); }
        }

        public string Status
        {
            get { return _status; }
            set { _status = value; Raise("Status"); }
        }

        public string Icon
        {
            get
            {
                switch (_state)
                {
                    case StepState.Running: return "•";
                    case StepState.Done: return "✓";
                    case StepState.Warning: return "!";
                    case StepState.Failed: return "×";
                    case StepState.Skipped: return "–";
                    default: return "";
                }
            }
        }

        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class LogLine
    {
        public LogLine(string time, string text, string level)
        {
            Time = time;
            Text = text;
            Level = level;
        }

        public string Time { get; private set; }
        public string Text { get; private set; }
        public string Level { get; private set; }
    }
}
