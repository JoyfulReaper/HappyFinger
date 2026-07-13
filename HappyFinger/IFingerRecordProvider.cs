namespace HappyFinger;

public interface IFingerRecordProvider
{
    IReadOnlyCollection<FingerRecord> GetDirectory();

    FingerRecord? Find(string name);
}
