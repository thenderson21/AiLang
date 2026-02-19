namespace AiVM.Core;

public readonly record struct VmUiEvent(
    string Type,
    string TargetId,
    int X,
    int Y,
    string Key,
    string Text,
    string Modifiers,
    bool Repeat);
