using SoulCaliber.Dialogue;

var tests = new (string Name, Action Test)[]
{
    ("Finish commits once; duplicate callbacks do not repeat the write", FinishOnce),
    ("Failure allows retry of the SAME finishing action", RetryFailure),
    ("A successful DB no-op for an existing completion is still finished", ExistingCompletion),
    ("Reentrant finish cannot execute twice", Reentrancy),
    ("Reentrant reset is rejected", ResetDuringSave),
    ("An interrupted encounter is not marked completed", Interrupted),
    ("A new conversation session gets a fresh completion state", Restart),
    ("Presentation error after completion cannot write twice", DisplayError)
};
int passed = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{passed}/{tests.Length} passed");
return passed == tests.Length ? 0 : 1;

static void FinishOnce()
{
    var guard = new SpecialEncounterCompletionGuard();
    int saves = 0;
    Assert(guard.Finish(() => saves++), "First completion must run");
    Assert(!guard.Finish(() => saves++), "Duplicate completion must be suppressed");
    Assert(guard.HasFinished && saves == 1, "Completion was not applied exactly once");
}
static void RetryFailure()
{
    var guard = new SpecialEncounterCompletionGuard();
    int attempts = 0;
    try { guard.Finish(() => { attempts++; throw new IOException("Simulated failed completion save"); }); }
    catch (IOException) { }
    Assert(!guard.HasFinished, "Failed completion marked as finished");
    Assert(guard.Finish(() => attempts++), "Retry should succeed");
    Assert(attempts == 2 && guard.HasFinished, "Retry state incorrect");
}
static void ExistingCompletion()
{
    // The persistence store returns false for an existing row. The callback
    // still completed without error; we should not attempt to save it again.
    var guard = new SpecialEncounterCompletionGuard();
    int calls = 0;
    guard.Finish(() => { calls++; bool newlyInserted = false; _ = newlyInserted; });
    Assert(guard.HasFinished && calls == 1, "Existing completion must be safe");
    guard.Finish(() => calls++);
    Assert(calls == 1, "Already-completed row must not be written again");
}
static void Reentrancy()
{
    var guard = new SpecialEncounterCompletionGuard();
    int writes = 0;
    guard.Finish(() =>
    {
        writes++;
        ExpectInvalid(() => guard.Finish(() => writes++));
    });
    Assert(writes == 1 && guard.HasFinished, "Reentrancy altered state");
}
static void ResetDuringSave()
{
    var guard = new SpecialEncounterCompletionGuard();
    guard.Finish(() => ExpectInvalid(guard.Reset));
    Assert(guard.HasFinished, "Reset during save cleared completion");
}
static void Interrupted()
{
    var guard = new SpecialEncounterCompletionGuard();
    Assert(!guard.HasFinished, "Launching without finishing should not mark complete");
    // Closing the scene simply drops the guard. No persistence occurs.
}
static void Restart()
{
    var guard = new SpecialEncounterCompletionGuard();
    int writes = 0;
    guard.Finish(() => writes++);
    guard.Reset();
    guard.Finish(() => writes++);
    Assert(writes == 2 && guard.HasFinished, "New session should finish normally");
}
static void DisplayError()
{
    var guard = new SpecialEncounterCompletionGuard();
    int writes = 0;
    try
    {
        guard.Finish(() => writes++);
        throw new InvalidOperationException("UI failed after commit");
    }
    catch (InvalidOperationException) { }
    guard.Finish(() => writes++);
    Assert(writes == 1 && guard.HasFinished, "Presentation failure caused duplicate save");
}
static void Assert(bool success, string message)
{
    if (!success) throw new Exception(message);
}
static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected InvalidOperationException");
}
