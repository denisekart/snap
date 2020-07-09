namespace Snap.Core
{
    public interface ITargetRunner
    {
        public string Type { get; }
        void Pack(SnapConfiguration.Target target);
        void Restore(SnapConfiguration.Target target);
        void Clean(SnapConfiguration.Target target);
    }
}
