namespace JamesOptimizer
{
    /// <summary>
    /// Contract for all V3 Liquid Dark navigable pages
    /// </summary>
    public interface INavigablePage
    {
        void OnNavigatedTo();
        void OnNavigatedFrom();
    }
}
