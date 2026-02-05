using OneOf;

namespace Telebot.Errors;

public sealed record Error(string Message, Exception? Exception = null);

public sealed class DownloadResult(OneOf<string?, Error> input) : OneOfBase<string?, Error>(input);