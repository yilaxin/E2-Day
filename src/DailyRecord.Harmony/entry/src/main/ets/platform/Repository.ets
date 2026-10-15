import { relationalStore } from '@kit.ArkData';
import { common } from '@kit.AbilityKit';
import { util } from '@kit.ArkTS';
import { AppState, TaskRecord, LocalAccount, ExchangeImage, ImportPlan, copyState, validateState, today, dateString, referencedImages } from '../domain/Models';
import { fingerprint, previewImport, resolveImport, readExchange, writeExchange } from '../domain/Exchange';
import { occurrences, leadDays, LunarDates } from '../domain/Tasks';
import { NativeCrypto } from './Crypto';

export class SavedCopy { Id: string = ''; At: string = ''; Count: number = 0; }
export class Repository {
  readonly crypto: NativeCrypto = new NativeCrypto();
  state: AppState = new AppState(); account: LocalAccount | null = null; lunar: LunarDates[] = [];
  private db: relationalStore.RdbStore | null = null;
  private tail: Promise<void> = Promise.resolve();
  private store(): relationalStore.RdbStore { if (!this.db) throw new Error('本机存储还未准备好。'); return this.db; }
  private queue(work: () => Promise<void>): Promise<void> {
    const result = this.tail.then(work); this.tail = result.catch(() => {}); return result;
  }
  private owner(): string { if (!this.account) throw new Error('请先登录本机账号。'); return this.account.Id; }
  private async value(owner: string, key: string): Promise<string | null> {
    const rows = await this.store().querySql('SELECT content FROM records WHERE owner = ? AND key = ?', [owner, key]);
    try { return rows.goToFirstRow() ? rows.getString(0) : null; } finally { rows.close(); }
  }
  private async setValue(owner: string, key: string, content: string): Promise<void> {
    await this.store().executeSql('INSERT OR REPLACE INTO records(owner, key, content) VALUES(?, ?, ?)', [owner, key, content]);
  }
  private async hasValue(owner: string, key: string): Promise<boolean> {
    const rows = await this.store().querySql('SELECT 1 FROM records WHERE owner = ? AND key = ?', [owner, key]);
    try { return rows.goToFirstRow(); } finally { rows.close(); }
  }
  async open(context: common.UIAbilityContext): Promise<void> {
    this.db = await relationalStore.getRdbStore(context, { name: 'e2day-native.db', securityLevel: relationalStore.SecurityLevel.S2 });
    await this.store().executeSql('CREATE TABLE IF NOT EXISTS records(owner TEXT NOT NULL, key TEXT NOT NULL, content TEXT NOT NULL, PRIMARY KEY(owner, key))');
    await this.store().executeSql('CREATE TABLE IF NOT EXISTS accounts(id TEXT PRIMARY KEY, name TEXT NOT NULL UNIQUE, content TEXT NOT NULL)');
    const bytes = await context.resourceManager.getRawFileContent('lunar-dates.json');
    this.lunar = JSON.parse(new util.TextDecoder().decodeToString(bytes)) as LunarDates[];
    const active = await this.value('', 'active');
    if (active) {
      const accounts = await this.accounts(); const account = accounts.find(row => row.Id === active);
      if (account) { const state = await this.value(account.Id, 'state'); if (state) { this.state = validateState(JSON.parse(state) as AppState); this.account = account; } }
    }
    if (this.account) await this.ensureUpcoming();
  }
  async accounts(): Promise<LocalAccount[]> {
    const rows = await this.store().querySql('SELECT content FROM accounts ORDER BY name'); const values: LocalAccount[] = [];
    try { while (rows.goToNextRow()) values.push(JSON.parse(rows.getString(0)) as LocalAccount); } finally { rows.close(); } return values;
  }
  async create(name: string, password: string, confirmation: string): Promise<string> {
    const normal = name.trim().normalize('NFC');
    if (normal.length < 2 || normal.length > 40 || /[\u0000-\u001F\u007F-\u009F]/.test(normal)) throw new Error('账号名需为 2 至 40 个字符。');
    if (password.length < 8 || password.length > 128 || password !== confirmation) throw new Error('密码需为 8 至 128 个字符，且两次输入相同。');
    const account = new LocalAccount(); account.Id = this.crypto.id(); account.Name = normal; account.Salt = this.crypto.random(16);
    const code = this.crypto.random(20); account.PasswordHash = await this.crypto.password(password, account.Salt); account.RecoveryHash = await this.crypto.hashText(code);
    await this.queue(async () => {
      if ((await this.accounts()).some(row => row.Name === normal)) throw new Error('此设备上已有同名账号。');
      const db = this.store(); db.beginTransaction();
      try {
        await db.executeSql('INSERT INTO accounts(id,name,content) VALUES(?,?,?)', [account.Id, account.Name, JSON.stringify(account)]);
        await this.setValue(account.Id, 'state', JSON.stringify(new AppState())); await this.setValue('', 'active', account.Id); db.commit();
      } catch (error) { db.rollBack(); throw error as Error; }
      this.account = account; this.state = new AppState();
    }); return code;
  }
  async login(id: string, password: string): Promise<void> {
    await this.queue(async () => {
      const account = (await this.accounts()).find(row => row.Id === id); if (!account) throw new Error('本机账号不存在。');
      if (!this.crypto.equals(await this.crypto.password(password, account.Salt), account.PasswordHash)) throw new Error('密码不正确。');
      const text = await this.value(id, 'state'); if (!text) throw new Error('本机记录不完整，请从自己的备份恢复。');
      const next = validateState(JSON.parse(text) as AppState); await this.setValue('', 'active', id); this.account = account; this.state = next;
    }); await this.ensureUpcoming();
  }
  async resetPassword(id: string, recovery: string, password: string, confirmation: string): Promise<string> {
    if (password.length < 8 || password.length > 128 || password !== confirmation) throw new Error('请填写 8 至 128 位新密码，并确认两次相同。');
    const code = this.crypto.random(20);
    await this.queue(async () => {
      const account = (await this.accounts()).find(row => row.Id === id); if (!account) throw new Error('账号不存在。');
      if (!this.crypto.equals(await this.crypto.hashText(recovery.trim().toUpperCase()), account.RecoveryHash)) throw new Error('恢复码不正确。');
      const next = JSON.parse(JSON.stringify(account)) as LocalAccount; next.Salt = this.crypto.random(16); next.PasswordHash = await this.crypto.password(password, next.Salt); next.RecoveryHash = await this.crypto.hashText(code);
      await this.store().executeSql('UPDATE accounts SET content = ? WHERE id = ?', [JSON.stringify(next), id]);
      if (this.account && this.account.Id === id) this.account = next;
    }); return code;
  }
  async lock(): Promise<void> { await this.queue(async () => { await this.setValue('', 'active', ''); this.account = null; this.state = new AppState(); }); }
  async mutate(change: (state: AppState) => void, images: ExchangeImage[] = []): Promise<void> {
    const owner = this.owner(); await this.queue(async () => {
      if (this.owner() !== owner) throw new Error('账号已切换，请重新打开操作。');
      const next = copyState(this.state); change(next); validateState(next); await this.persist(owner, next, images, false); this.state = next;
    });
  }
  private async persist(owner: string, state: AppState, images: ExchangeImage[], before: boolean): Promise<void> {
    const db = this.store(); const available = images.map(row => row.Id);
    for (const id of referencedImages(state)) if (!available.includes(id) && !await this.hasValue(owner, 'image:' + id)) throw new Error('所需图片缺失，修改尚未保存。');
    db.beginTransaction();
    try {
      if (before) await this.setValue(owner, 'before:' + new Date().toISOString() + ':' + this.crypto.id(), JSON.stringify(this.state));
      for (const image of images) await this.setValue(owner, 'image:' + image.Id, JSON.stringify(image));
      await this.setValue(owner, 'state', JSON.stringify(state)); db.commit();
    } catch (error) { db.rollBack(); throw error as Error; }
  }
  async ensureUpcoming(): Promise<void> {
    if (!this.account) return;
    const owner = this.owner(); await this.queue(async () => {
      if (this.owner() !== owner) return; const next = copyState(this.state); let changed = false;
      for (const event of next.Events) {
        const from = event.Repeat === 'Daily' ? today() : event.StartDate; const to = event.Repeat === 'Daily' ? today() : this.future(leadDays(next, event));
        for (const date of occurrences(event, from, to, this.lunar)) if (!next.Tasks.some(row => row.EventId === event.Id && row.OccurrenceDate === date)) {
          const task = new TaskRecord(); task.Id = (await this.crypto.hashText(event.Id + ':' + date)).slice(0, 32).toLowerCase();
          task.EventId = event.Id; task.OccurrenceDate = date; task.ScheduledDate = date; task.OriginalScheduledDate = date;
          task.Title = event.Title; task.Notes = event.Notes; task.ImageIds = event.ImageIds.slice(); task.EventImportance = event.Importance; task.IsImportant = event.IsImportant; task.CreatedAt = new Date().toISOString();
          next.Tasks.push(task); changed = true;
        }
      }
      if (changed) { validateState(next); await this.persist(owner, next, [], false); this.state = next; }
    });
  }
  private future(days: number): string { const date = new Date(); date.setDate(date.getDate() + days); return dateString(date); }
  async image(id: string): Promise<ExchangeImage> { const text = await this.value(this.owner(), 'image:' + id); if (!text) throw new Error('本机图片缺失。'); return JSON.parse(text) as ExchangeImage; }
  async exportFile(): Promise<string> {
    await this.tail; const owner = this.owner(); const state = copyState(this.state); const images: ExchangeImage[] = [];
    for (const id of referencedImages(state)) images.push(await this.image(id));
    if (this.owner() !== owner) throw new Error('账号已切换，请重新导出。'); return writeExchange(state, images, this.crypto);
  }
  async importPreview(text: string): Promise<ImportPlan> {
    const owner = this.owner(); const file = await readExchange(text, this.crypto); await this.tail;
    if (this.owner() !== owner) throw new Error('账号已切换，请重新选择文件。');
    const plan = previewImport(this.state, JSON.parse(file.Payload) as AppState); plan.AccountId = owner; plan.Images = file.Images; return plan;
  }
  async importCommit(plan: ImportPlan, choices: string[]): Promise<void> {
    await this.queue(async () => {
      if (this.owner() !== plan.AccountId || fingerprint(this.state) !== plan.Fingerprint) throw new Error('账号或记录已有变化，请重新预览。');
      const next = resolveImport(plan, choices); await this.persist(plan.AccountId, next, plan.Images, true); this.state = next;
    }); await this.ensureUpcoming();
  }
  async copies(): Promise<SavedCopy[]> {
    await this.tail; const rows = await this.store().querySql('SELECT key,content FROM records WHERE owner = ? AND key LIKE ? ORDER BY key DESC', [this.owner(), 'before:%']); const values: SavedCopy[] = [];
    try { while (rows.goToNextRow()) { const item = new SavedCopy(); item.Id = rows.getString(0); item.At = item.Id.slice(7, 31); item.Count = (JSON.parse(rows.getString(1)) as AppState).Tasks.length; values.push(item); } } finally { rows.close(); } return values;
  }
  async restoreCopy(id: string): Promise<void> {
    const owner = this.owner(); await this.queue(async () => {
      if (this.owner() !== owner || !id.startsWith('before:')) throw new Error('账号已变化。');
      const text = await this.value(owner, id); if (!text) throw new Error('副本不存在。');
      const next = validateState(JSON.parse(text) as AppState); await this.persist(owner, next, [], true); this.state = next;
    });
  }
}
