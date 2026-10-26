using SportClubLibrary.Interfaces;
using SportClubLibrary.Utilities;
using System.Text.Json;

namespace SportClubLibrary.Repositories
{
    public class JsonRepository<T> : IAsyncRepository<T> where T : class, IEntity
    {
        private readonly string _filePath;
        private readonly ILogger _logger;
        private List<T> _items = new();

        private readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public JsonRepository(string fileName, ILogger logger)
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            _logger = logger;
        }

        private async Task LoadDataAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    await _logger.LogAsync($"Файл {_filePath} не знайдено. Створюється новий.");
                    _items = new List<T>();
                    return;
                }

                string json = await File.ReadAllTextAsync(_filePath);
                _items = JsonSerializer.Deserialize<List<T>>(json, _options) ?? new List<T>();
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Помилка завантаження {_filePath}: {ex.Message}", LogType.Error);
                _items = new List<T>();
            }
        }

        private async Task SaveDataAsync()
        {
            try
            {
                string json = JsonSerializer.Serialize(_items, _options);
                await File.WriteAllTextAsync(_filePath, json);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"Помилка збереження {_filePath}: {ex.Message}", LogType.Error);
            }
        }

        public async Task ReloadAsync()
        {
            _items.Clear();
            await LoadDataAsync();
        }

        public Task<IEnumerable<T>> GetAllAsync()
        {
            return Task.FromResult<IEnumerable<T>>(_items);
        }

        public Task<T> GetByIdAsync(int id)
        {
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
        }

        public async Task AddAsync(T item)
        {
            item.Id = _items.Count == 0 ? 1 : _items.Max(x => x.Id) + 1;
            _items.Add(item);
            await SaveDataAsync();
            await _logger.LogAsync($"Додано ID {item.Id}");
        }

        public async Task UpdateAsync(T item)
        {
            int index = _items.FindIndex(x => x.Id == item.Id);

            if (index >= 0)
            {
                _items[index] = item;
                await SaveDataAsync();
                await _logger.LogAsync($"Оновлено ID {item.Id}");
            }
            else
            {
                await _logger.LogAsync($"Оновлення неіснуючого ID {item.Id}", LogType.Warn);
            }
        }

        public async Task DeleteAsync(int id)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);

            if (item != null)
            {
                _items.Remove(item);
                await SaveDataAsync();
                await _logger.LogAsync($"Видалено ID {id}");
            }
            else
            {
                await _logger.LogAsync($"Спроба видалити неіснуючий ID {id}", LogType.Warn);
            }
        }
    }
}