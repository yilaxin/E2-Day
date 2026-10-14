using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DailyRecord.Core;

public sealed class LocalAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Salt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RecoveryHash { get; set; } = "";
    public List<BoundDevice> Devices { get; set; } = [];
}
public sealed class BoundDevice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record AccountSession(string AccountId, string DeviceId, string Token);
public sealed record CreatedAccount(LocalAccount Account, string RecoveryCode);

// No remote identity provider: account identifiers, salted verifiers and device grants stay on the hub.
public sealed class AccountVault
{
    public const int PasswordIterations = 600_000;
    private readonly string _path;
    private readonly object _gate = new();
    private readonly List<LocalAccount> _accounts;
    public string Root { get; }
    public IReadOnlyList<LocalAccount> Accounts { get { lock (_gate) return _accounts.ToArray(); } }
    public AccountVault(string root)
    {
        Root = Path.GetFullPath(root); Directory.CreateDirectory(Root);
        _path = Path.Combine(Root, "accounts.json");
        _accounts = File.Exists(_path) ? JsonSerializer.Deserialize<List<LocalAccount>>(File.ReadAllText(_path), StateJson.Options)
            ?? throw new InvalidDataException("账号文件无效，原有记录仍在账号目录内。") : [];
        if (_accounts.Any(a => a is null || !Guid.TryParseExact(a.Id, "N", out _) || string.IsNullOrWhiteSpace(a.Name)) ||
            _accounts.Select(a => a.Id).Distinct().Count() != _accounts.Count)
            throw new InvalidDataException("账号文件无效。");
    }
    public string DataDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("账号标识无效。");
        return Path.Combine(Root, "Accounts", id);
    }
    public CreatedAccount Create(string name, string password, Action<LocalAccount>? prepareData = null)
    {
        name = name.Trim().Normalize();
        if (name.Length is < 2 or > 40 || name.Any(char.IsControl)) throw new ArgumentException("账号名需为 2 至 40 个字符。");
        ValidatePassword(password);
        lock (_gate)
        {
            if (_accounts.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("账号名已存在。");
            var recovery = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
            var account = new LocalAccount { Name = name, RecoveryHash = Hash(recovery) };
            SetPassword(account, password); prepareData?.Invoke(account); _accounts.Add(account);
            try { Persist(); } catch { _accounts.Remove(account); throw; }
            return new(account, recovery);
        }
    }
    public LocalAccount Login(string name, string password)
    {
        lock (_gate)
        {
            var account = _accounts.FirstOrDefault(a => a.Name.Equals(name.Trim().Normalize(), StringComparison.OrdinalIgnoreCase));
            if (account is null || !VerifyPassword(account, password)) throw new UnauthorizedAccessException("账号或密码不正确。");
            return account;
        }
    }
    public AccountSession Bind(string accountId, string deviceName)
    {
        lock (_gate)
        {
            var account = Find(accountId); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var device = new BoundDevice { Name = string.IsNullOrWhiteSpace(deviceName) ? "已绑定设备" : deviceName.Trim()[..Math.Min(80, deviceName.Trim().Length)], TokenHash = Hash(token) };
            account.Devices.Add(device); Persist(); return new(accountId, device.Id, token);
        }
    }
    public LocalAccount Authenticate(string token, string? accountId = null)
    {
        if (token.Length != 64) throw new UnauthorizedAccessException("请登录或重新绑定设备。");
        var hash = Hash(token);
        lock (_gate)
        {
            var account = _accounts.FirstOrDefault(a => (accountId is null || a.Id == accountId) && a.Devices.Any(d => Same(d.TokenHash, hash)));
            return account ?? throw new UnauthorizedAccessException("设备已退出登录或取消绑定。");
        }
    }
    public string ResetPassword(string accountId, string password, string? recoveryCode = null)
    {
        ValidatePassword(password);
        lock (_gate)
        {
            var account = Find(accountId);
            if (recoveryCode is not null && !Same(account.RecoveryHash, Hash(recoveryCode.Replace(" ", "").Replace("-", "").ToUpperInvariant())))
                throw new UnauthorizedAccessException("恢复码不正确。");
            var recovery = Convert.ToHexString(RandomNumberGenerator.GetBytes(20));
            SetPassword(account, password); account.RecoveryHash = Hash(recovery);
            if (recoveryCode is not null) account.Devices.Clear();
            Persist(); return recovery;
        }
    }
    public void Revoke(string accountId, string deviceId)
    { lock (_gate) { Find(accountId).Devices.RemoveAll(d => d.Id == deviceId); Persist(); } }
    public static bool VerifyPassword(LocalAccount account, string password) => password.Length <= 128 && Same(account.PasswordHash, PasswordHash(password, account.Salt));
    private LocalAccount Find(string id) => _accounts.SingleOrDefault(a => a.Id == id) ?? throw new UnauthorizedAccessException("账号不存在。");
    public static void ValidatePassword(string password)
    { if (password.Length is < 8 or > 128) throw new ArgumentException("密码需为 8 至 128 个字符。"); }
    private static void SetPassword(LocalAccount account, string password)
    { account.Salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); account.PasswordHash = PasswordHash(password, account.Salt); }
    private static string PasswordHash(string password, string salt) => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromHexString(salt), PasswordIterations, HashAlgorithmName.SHA256, 32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Same(string a, string b) => a.Length == b.Length && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));
    private void Persist()
    {
        var temp = _path + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(_accounts, StateJson.Options)); File.Move(temp, _path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
