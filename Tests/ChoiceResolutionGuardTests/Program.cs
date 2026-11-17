using SoulCaliber.Dialogue;

var tests = new (string Name, Action Run)[]
{
    ("First selection commits once; repeated callbacks reuse the result", CommitsOnlyOnce),
    ("Failed persistence retries the original roll and outcome", FailedCommitKeepsRoll),
    ("Cannot switch options to reroll after a failed save", CannotSwitchAfterFailure),
    ("Cannot switch options after a successful save", CannotSwitchAfterSuccess),
    ("Advancing/restarting permits a new legitimate resolution", NewRoundMayCommitAgain),
    ("Concurrent/reentrant calls cannot commit twice", ReentrantCallsAreRejected),
    ("A preparation failure does not poison the choice state", PreparationFailureCanRetry),
    ("UI error after commit cannot cause outcome reapplication", DisplayFailureCannotReplay)
};

int passed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{passed}/{tests.Length} tests passed");
return passed == tests.Length ? 0 : 1;

static void CommitsOnlyOnce()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    int prepared = 0, commits = 0;
    Receipt Execute() => guard.Execute("ROUND_1", "INTIMIDATE",
        () => { prepared++; return new Prepared(54); },
        p => { commits++; return new Receipt(p.Roll); });
    var first = Execute();
    var second = Execute();
    Assert(prepared == 1 && commits == 1, "Duplicate callback repeated work");
    Assert(ReferenceEquals(first, second), "Committed result should be reused");
    Assert(guard.HasCommittedChoice, "Committed state not retained");
}

static void FailedCommitKeepsRoll()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    int prepared = 0, commits = 0;
    try
    {
        guard.Execute("ROUND_1", "INTIMIDATE",
            () => { prepared++; return new Prepared(86); },
            p => { commits++; throw new IOException("Simulated SQLite rollback"); });
        throw new Exception("Expected database failure");
    }
    catch (IOException) { }
    Assert(!guard.HasCommittedChoice && guard.PendingOptionKey == "INTIMIDATE",
        "Failed save must remain pending");
    var result = guard.Execute("ROUND_1", "INTIMIDATE",
        () => { prepared++; return new Prepared(2); },
        p => { commits++; return new Receipt(p.Roll); });
    Assert(prepared == 1 && commits == 2 && result.Roll == 86,
        "Retry recomputed the roll or did not retry the commit");
}

static void CannotSwitchAfterFailure()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    try { guard.Execute("R", "A", () => new Prepared(50), _ => throw new IOException()); }
    catch (IOException) { }
    ExpectInvalid(() => guard.Execute("R", "B", () => new Prepared(1), p => new Receipt(p.Roll)));
}

static void CannotSwitchAfterSuccess()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    guard.Execute("R", "A", () => new Prepared(60), p => new Receipt(p.Roll));
    ExpectInvalid(() => guard.Execute("R", "B", () => new Prepared(1), p => new Receipt(p.Roll)));
}

static void NewRoundMayCommitAgain()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    int writes = 0;
    guard.Execute("R1", "A", () => new Prepared(30), p => { writes++; return new Receipt(p.Roll); });
    guard.Clear();
    guard.Execute("R2", "A", () => new Prepared(40), p => { writes++; return new Receipt(p.Roll); });
    Assert(writes == 2, "New round incorrectly suppressed");
}

static void ReentrantCallsAreRejected()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    var receipt = guard.Execute("R", "A", () => new Prepared(20), p =>
    {
        ExpectInvalid(() => guard.Execute("R", "A", () => new Prepared(99), x => new Receipt(x.Roll)));
        ExpectInvalid(guard.Clear);
        return new Receipt(p.Roll);
    });
    Assert(receipt.Roll == 20 && guard.HasCommittedChoice, "Reentrancy guard failed");
}

static void PreparationFailureCanRetry()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    try { guard.Execute("R", "A", () => throw new FormatException(), p => new Receipt(p.Roll)); }
    catch (FormatException) { }
    Assert(guard.PendingOptionKey is null, "Failed preparation should not retain invalid facts");
    var result = guard.Execute("R", "A", () => new Prepared(10), p => new Receipt(p.Roll));
    Assert(result.Roll == 10, "Could not recover from preparation failure");
}

static void DisplayFailureCannotReplay()
{
    var guard = new ChoiceResolutionGuard<Prepared, Receipt>();
    int writes = 0;
    Receipt Resolve() => guard.Execute("R", "A", () => new Prepared(19), p =>
    {
        writes++;
        return new Receipt(p.Roll);
    });
    try { Resolve(); throw new InvalidOperationException("Portrait failed to load"); }
    catch (InvalidOperationException) { }
    var recovered = Resolve();
    Assert(writes == 1 && recovered.Roll == 19, "Committed effect executed twice after UI error");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void ExpectInvalid(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected an InvalidOperationException");
}

record Prepared(int Roll);
record Receipt(int Roll);
