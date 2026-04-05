namespace llm_cli.Tests.Fakes;

internal static class AsyncEnumerableHelpers
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
