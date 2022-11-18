namespace Gradinware.Models.Authentication
{
    internal enum UserEventCode
    {
        Created,
        LoginFailed,
        LoginSucceeded,
        Updated,
        PasswordChanged,
        PasswordResetRequested,
        PasswordResetCompleted,
        Locked,
    }
}
