// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.VFS;

namespace Itexoft.Tests.IO.VFS;

[TestFixture]
internal sealed class JournalManagerTest
{
    [Test]
    public void BeginTransaction_ShouldIncreaseTxId()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        var tx1 = journal.BeginTransaction();
        journal.RecordAllocateBlock(123);
        Assert.That(tx1, Is.Not.EqualTo(0), "tx1 should not be zero");

        journal.CommitTransaction();

        var tx2 = journal.BeginTransaction();
        Assert.That(tx2, Is.GreaterThan(tx1), "tx2 should be greater than tx1");

        journal.RecordReleaseBlock(456);
        journal.CommitTransaction();
    }

    [Test]
    public void BeginTransaction_WithoutCommit_ShouldNotPersistRecords()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        journal.BeginTransaction();
        journal.RecordAllocateBlock(1001);
        journal.RollbackTransaction(); // No commit

        var (existingTxId, records) = journal.GetAllRecords();
        Assert.That(existingTxId, Is.EqualTo(0));
        Assert.That(records, Is.Empty);
    }

    [Test]
    public void RecordAllocateBlock_ThenCommit_ShouldPersistRecord()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        var tx = journal.BeginTransaction();
        journal.RecordAllocateBlock(777);
        journal.CommitTransaction();

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(tx));
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].recordType, Is.EqualTo(0x10), "Expected allocate record type");
        Assert.That(records[0].blockIndex, Is.EqualTo(777));
    }

    [Test]
    public void RecordReleaseBlock_ThenCommit_ShouldPersistRecord()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        var tx = journal.BeginTransaction();
        journal.RecordReleaseBlock(999);
        journal.CommitTransaction();

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(tx));
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].recordType, Is.EqualTo(0x20), "Expected release record type");
        Assert.That(records[0].blockIndex, Is.EqualTo(999));
    }

    [Test]
    public void MultipleRecords_ShouldPersistAllAfterCommit()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        var tx = journal.BeginTransaction();
        journal.RecordAllocateBlock(100);
        journal.RecordAllocateBlock(200);
        journal.RecordReleaseBlock(300);
        journal.RecordReleaseBlock(400);
        journal.CommitTransaction();

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(tx));
        Assert.That(records, Has.Count.EqualTo(4));
        Assert.That(records[0].recordType, Is.EqualTo(0x10));
        Assert.That(records[0].blockIndex, Is.EqualTo(100));
        Assert.That(records[1].recordType, Is.EqualTo(0x10));
        Assert.That(records[1].blockIndex, Is.EqualTo(200));
        Assert.That(records[2].recordType, Is.EqualTo(0x20));
        Assert.That(records[2].blockIndex, Is.EqualTo(300));
        Assert.That(records[3].recordType, Is.EqualTo(0x20));
        Assert.That(records[3].blockIndex, Is.EqualTo(400));
    }

    [Test]
    public void CommitTransaction_IncrementsTxAndFlipsRegion()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        var tx1 = journal.BeginTransaction();
        journal.RecordAllocateBlock(1);
        journal.CommitTransaction();

        var tx2 = journal.BeginTransaction();
        journal.RecordAllocateBlock(2);
        journal.CommitTransaction();

        Assert.That(tx2, Is.GreaterThan(tx1));

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(tx2));
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].blockIndex, Is.EqualTo(2));
    }

    [Test]
    public void RollbackTransaction_ShouldDiscardPendingRecords()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        journal.BeginTransaction();
        journal.RecordAllocateBlock(555);
        journal.RollbackTransaction();

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(0));
        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Recovery_IgnoresUncommittedData()
    {
        using var memStream = new TestMemoryStream();

        {
            using var journal = new JournalManager(memStream);
            journal.BeginTransaction();
            journal.RecordAllocateBlock(9999);

            // No commit
        }

        using var journal2 = new JournalManager(memStream);
        var (readTxId, records) = journal2.GetAllRecords();

        Assert.That(readTxId, Is.EqualTo(0));
        Assert.That(records, Is.Empty);
    }

    [Test]
    public void Recovery_PreservesCommittedData()
    {
        using var memStream = new TestMemoryStream();

        {
            using var journal = new JournalManager(memStream);
            journal.BeginTransaction();
            journal.RecordAllocateBlock(1010);
            journal.CommitTransaction();
        }

        using var journal2 = new JournalManager(memStream);
        var (readTxId, records) = journal2.GetAllRecords();
        Assert.That(readTxId, Is.Not.EqualTo(0));
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].blockIndex, Is.EqualTo(1010));
    }

    [Test]
    public void ConcurrentTransactions_ShouldNotOverlapInSingleJournal()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        journal.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => journal.BeginTransaction());

        journal.RecordAllocateBlock(333);
        journal.CommitTransaction();
    }

    //[Test]
    //public void Concurrency_MultipleTasks_BasicLock()
    //{
    //    using var memStream = new TestMemoryStream();
    //    using var journal = new JournalManager(memStream);

    //    var tasks = new Task[5];
    //    for (int i = 0; i < tasks.Length; i++)
    //    {
    //        tasks[i] = Task.Run(() =>
    //        {
    //            try
    //            {
    //                lock (journal)
    //                {
    //                    var txId = journal.BeginTransaction();
    //                    journal.RecordAllocateBlock(txId);
    //                    journal.CommitTransaction();
    //                }
    //            }
    //            catch
    //            {
    //                // ignoring concurrency issues here
    //            }
    //        });
    //    }

    //    Task.WaitAll(tasks);

    //    var (_, records) = journal.GetAllRecords();
    //    // We expect up to 5 commits
    //    Assert.That(records.Count, Is.LessThanOrEqualTo(5));
    //}

    //[Test]
    //public void MultipleCommits_IncreasingTxId()
    //{
    //    using var memStream = new TestMemoryStream();
    //    using var journal = new JournalManager(memStream);

    //    long lastTxId = 0;
    //    for (int i = 0; i < 10; i++)
    //    {
    //        var tx = journal.BeginTransaction();
    //        journal.RecordAllocateBlock(i);
    //        journal.CommitTransaction();
    //        Assert.That(tx, Is.GreaterThan(lastTxId), $"Transaction {i} must have a greater ID than the previous");
    //        lastTxId = tx;
    //    }

    //    var (readTxId, records) = journal.GetAllRecords();
    //    Assert.That(readTxId, Is.EqualTo(lastTxId));
    //    Assert.That(records, Has.Count.EqualTo(10));
    //    for (int i = 0; i < 10; i++)
    //    {
    //        Assert.That(records[i].recordType, Is.EqualTo(0x10));
    //        Assert.That(records[i].blockIndex, Is.EqualTo(i));
    //    }
    //}

    [Test]
    public void BeginTransaction_TwiceInARow_Throws()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        journal.BeginTransaction();
        Assert.Throws<InvalidOperationException>(() => journal.BeginTransaction());
    }

    [Test]
    public void CommitTransaction_WithoutBegin_Throws()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        Assert.Throws<InvalidOperationException>(() => journal.CommitTransaction());
    }

    [Test]
    public void RollbackTransaction_WithoutBegin_Throws()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        Assert.Throws<InvalidOperationException>(() => journal.RollbackTransaction());
    }

    [Test]
    public void BeginAndRollback_ShouldNotPersistAnything()
    {
        using var memStream = new TestMemoryStream();
        using var journal = new JournalManager(memStream);

        journal.BeginTransaction();
        journal.RecordAllocateBlock(3000);
        journal.RecordReleaseBlock(4000);
        journal.RollbackTransaction();

        var (readTxId, records) = journal.GetAllRecords();
        Assert.That(readTxId, Is.EqualTo(0));
        Assert.That(records, Is.Empty);
    }

    //[Test]
    //public void CommitTwoTransactions_RecordsAreAppended()
    //{
    //    using var memStream = new TestMemoryStream();
    //    using var journal = new JournalManager(memStream);

    //    var tx1 = journal.BeginTransaction();
    //    journal.RecordAllocateBlock(111);
    //    journal.RecordReleaseBlock(222);
    //    journal.CommitTransaction();

    //    var tx2 = journal.BeginTransaction();
    //    journal.RecordAllocateBlock(333);
    //    journal.RecordReleaseBlock(444);
    //    journal.CommitTransaction();

    //    var (readTxId, records) = journal.GetAllRecords();
    //    Assert.That(readTxId, Is.EqualTo(tx2));
    //    Assert.That(records, Has.Count.EqualTo(4));

    //    Assert.That(records[0].recordType, Is.EqualTo(0x10));
    //    Assert.That(records[0].blockIndex, Is.EqualTo(111));
    //    Assert.That(records[1].recordType, Is.EqualTo(0x20));
    //    Assert.That(records[1].blockIndex, Is.EqualTo(222));
    //    Assert.That(records[2].recordType, Is.EqualTo(0x10));
    //    Assert.That(records[2].blockIndex, Is.EqualTo(333));
    //    Assert.That(records[3].recordType, Is.EqualTo(0x20));
    //    Assert.That(records[3].blockIndex, Is.EqualTo(444));
    //}
}
