using Google.Apis.Util.Store;

namespace FinancialAdvisorAI.API.Repositories
{
    public class NullDataStore : IDataStore
    {
        public Task StoreAsync<T>(string key, T value)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            return Task.FromResult(default(T));
        }

        public Task ClearAsync()
        {
            return Task.CompletedTask;
        }
    }
}
