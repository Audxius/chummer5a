using System;
using System.Threading;
using System.Threading.Tasks;
using Chummer;

/// <summary>
/// Screenshot runs are unattended: log message boxes and answer with the first button.
/// </summary>
internal sealed class LoggingMessageService : IMessageService
{
    public MessageResult Show(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
        MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1)
    {
        Console.WriteLine("[message] " + caption + ": " + message?.Replace('\n', ' '));
        switch (buttons)
        {
            case MessageButtons.YesNo:
            case MessageButtons.YesNoCancel:
                return MessageResult.Yes;
            case MessageButtons.RetryCancel:
                return MessageResult.Cancel;
            case MessageButtons.AbortRetryIgnore:
                return MessageResult.Ignore;
            default:
                return MessageResult.OK;
        }
    }

    public Task<MessageResult> ShowAsync(object owner, string message, string caption = null, MessageButtons buttons = MessageButtons.OK,
        MessageIcon icon = MessageIcon.None, MessageDefaultButton defaultButton = MessageDefaultButton.Button1, CancellationToken token = default)
        => Task.FromResult(Show(owner, message, caption, buttons, icon, defaultButton));
}
