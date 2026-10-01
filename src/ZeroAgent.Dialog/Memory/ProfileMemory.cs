using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ZeroAgent.Dialog.Memory
{
    public enum UserRole
    {
        Operator,
        Technician,
        Engineer,
        Supervisor,
        Admin
    }

    public sealed class UserProfile
    {
        public string UserId { get; }
        public string Name { get; }
        public UserRole Role { get; }
        public HashSet<string> Permissions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> AssignedAreas { get; } = new List<string>();
        public UserPersona Persona { get; } = new UserPersona();

        public UserProfile(string userId, string name, UserRole role)
        {
            UserId = userId ?? throw new ArgumentNullException(nameof(userId));
            Name = name ?? string.Empty;
            Role = role;

            // Default role permissions
            switch (role)
            {
                case UserRole.Operator:
                    Permissions.Add("READ_STATUS");
                    Permissions.Add("QUERY_TELEMETRY");
                    break;
                case UserRole.Technician:
                    Permissions.Add("READ_STATUS");
                    Permissions.Add("QUERY_TELEMETRY");
                    Permissions.Add("DIAGNOSE_FAULTS");
                    Permissions.Add("RESET_FAULTS");
                    break;
                case UserRole.Engineer:
                case UserRole.Supervisor:
                case UserRole.Admin:
                    Permissions.Add("READ_STATUS");
                    Permissions.Add("QUERY_TELEMETRY");
                    Permissions.Add("DIAGNOSE_FAULTS");
                    Permissions.Add("RESET_FAULTS");
                    Permissions.Add("WRITE_PLC");
                    Permissions.Add("ACTUATE_RELAY");
                    Permissions.Add("STOP_MACHINE");
                    break;
            }
        }

        public bool CanExecute(string actionPermission)
        {
            if (string.IsNullOrEmpty(actionPermission)) return true;
            if (Role == UserRole.Admin) return true;
            return Permissions.Contains(actionPermission);
        }
    }

    /// <summary>
    /// Profile Memory (User & Role Context Memory).
    /// Enforces Role-Based Access Control (RBAC) and safety boundaries for conversational actions.
    /// </summary>
    public sealed class ProfileMemory
    {
        private readonly ConcurrentDictionary<string, UserProfile> _profiles = new ConcurrentDictionary<string, UserProfile>(StringComparer.OrdinalIgnoreCase);

        public void SaveProfile(UserProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            _profiles[profile.UserId] = profile;
        }

        public UserProfile GetOrCreate(string userId, string name = "Operator", UserRole defaultRole = UserRole.Operator)
        {
            return _profiles.GetOrAdd(userId, id => new UserProfile(id, name, defaultRole));
        }
    }
}
