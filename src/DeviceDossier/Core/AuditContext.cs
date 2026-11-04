using System;
using System.Collections.Generic;
using System.Threading;

namespace DeviceDossier.Core
{
    public enum LogLevel { Info, Ok, Warning, Error }

    public sealed class AuditProgress
    {
        public AuditProgress(string message, LogLevel level, int step, int totalSteps, string stepId)
        {
            Message = message;
            Level = level;
            Step = step;
            TotalSteps = totalSteps;
            StepId = stepId;
            Time = DateTime.Now;
        }

        public string Message { get; private set; }
        public LogLevel Level { get; private set; }
        public int Step { get; private set; }
        public int TotalSteps { get; private set; }
        public string StepId { get; private set; }
        public DateTime Time { get; private set; }
    }

    public sealed class UserProfile
    {
        public string Sid { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public sealed class AuditContext
    {
        private readonly IProgress<AuditProgress> _progress;

        public AuditContext(AuditSettings settings, AuditOptions options, IProgress<AuditProgress> progress, CancellationToken token)
        {
            Settings = settings;
            Options = options;
            _progress = progress;
            Token = token;
            Profiles = new List<UserProfile>();
        }

        public AuditSettings Settings { get; private set; }
        public AuditOptions Options { get; private set; }
        public CancellationToken Token { get; private set; }
        public bool IsAdmin { get; set; }
        public List<UserProfile> Profiles { get; private set; }
        public int Step { get; set; }
        public int TotalSteps { get; set; }
        public string StepId { get; set; }

        public void Info(string message) { Report(message, LogLevel.Info); }
        public void Ok(string message) { Report(message, LogLevel.Ok); }
        public void Warn(string message) { Report(message, LogLevel.Warning); }
        public void Error(string message) { Report(message, LogLevel.Error); }

        public void Report(string message, LogLevel level)
        {
            if (_progress != null) _progress.Report(new AuditProgress(message, level, Step, TotalSteps, StepId));
        }
    }
}
