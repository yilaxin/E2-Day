import { cryptoFramework } from '@kit.CryptoArchitectureKit';
import { util } from '@kit.ArkTS';
import { CryptoPort } from '../domain/Models';
export class NativeCrypto implements CryptoPort {
  private encoder: util.TextEncoder = new util.TextEncoder();
  private base64: util.Base64Helper = new util.Base64Helper();
  hex(bytes: Uint8Array): string { return Array.from(bytes).map(value => value.toString(16).padStart(2, '0')).join('').toUpperCase(); }
  unhex(text: string): Uint8Array {
    if (!/^(?:[A-Fa-f0-9]{2})+$/.test(text)) throw new Error('账号校验信息不完整。');
    const bytes = new Uint8Array(text.length / 2); for (let index = 0; index < bytes.length; index++) bytes[index] = parseInt(text.slice(index * 2, index * 2 + 2), 16); return bytes;
  }
  random(size: number): string { return this.hex(cryptoFramework.createRandom().generateRandomSync(size).data); }
  id(): string { return this.random(16).toLowerCase(); }
  async hashBytes(bytes: Uint8Array): Promise<string> {
    const md = cryptoFramework.createMd('SHA256'); await md.update({ data: bytes }); return this.hex((await md.digest()).data);
  }
  async hashText(text: string): Promise<string> { return this.hashBytes(this.encoder.encodeInto(text)); }
  decodeBase64(text: string): Uint8Array { return this.base64.decodeSync(text); }
  encodeBase64(bytes: Uint8Array): string { return this.base64.encodeToStringSync(bytes); }
  async password(password: string, salt: string): Promise<string> {
    const spec: cryptoFramework.PBKDF2Spec = { algName: 'PBKDF2', password: password, salt: this.unhex(salt), iterations: 600000, keySize: 32 };
    return this.hex((await cryptoFramework.createKdf('PBKDF2|SHA256').generateSecret(spec)).data);
  }
  equals(a: string, b: string): boolean {
    if (a.length !== b.length) return false; let different = 0; for (let index = 0; index < a.length; index++) different |= a.charCodeAt(index) ^ b.charCodeAt(index); return different === 0;
  }
}
