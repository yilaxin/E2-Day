import { common } from '@kit.AbilityKit';
import { fileIo as fs, picker } from '@kit.CoreFileKit';
import { photoAccessHelper } from '@kit.MediaLibraryKit';
import { image } from '@kit.ImageKit';
import { util } from '@kit.ArkTS';
import { ExchangeImage, today } from '../domain/Models';
import { NativeCrypto } from './Crypto';

export class NativeFiles {
  private context: common.UIAbilityContext;
  private crypto: NativeCrypto;
  constructor(context: common.UIAbilityContext, crypto: NativeCrypto) { this.context = context; this.crypto = crypto; }
  async chooseExchange(): Promise<string | null> {
    const options = new picker.DocumentSelectOptions(); options.maxSelectNumber = 1;
    const uris = await new picker.DocumentViewPicker(this.context).select(options); if (!uris.length) return null;
    const file = fs.openSync(uris[0], fs.OpenMode.READ_ONLY);
    try {
      const size = fs.statSync(file.fd).size; if (size > 512000000 || size < 2) throw new Error('请选择不超过 512 MB 的交换文件。');
      const bytes = new Uint8Array(size); let offset = 0;
      while (offset < size) {
        const buffer = new ArrayBuffer(Math.min(65536, size - offset)); const count = fs.readSync(file.fd, buffer, { offset: offset, length: buffer.byteLength });
        if (!count) throw new Error('文件未完整读取，请重新选择。'); bytes.set(new Uint8Array(buffer, 0, count), offset); offset += count;
      }
      return new util.TextDecoder('utf-8', { fatal: true }).decodeToString(bytes);
    } finally { fs.closeSync(file); }
  }
  async saveText(text: string, fileName: string = `E2-Day-交换文件-${today()}.json`): Promise<boolean> {
    const options = new picker.DocumentSaveOptions(); options.newFileNames = [fileName];
    const uris = await new picker.DocumentViewPicker(this.context).save(options); if (!uris.length) return false;
    const file = fs.openSync(uris[0], fs.OpenMode.READ_WRITE | fs.OpenMode.CREATE | fs.OpenMode.TRUNC);
    try {
      const bytes = new util.TextEncoder().encodeInto(text); let offset = 0;
      while (offset < bytes.byteLength) { const remaining = bytes.slice(offset); const count = fs.writeSync(file.fd, remaining.buffer); if (!count) throw new Error('文件未完整写入，请重新导出。'); offset += count; }
      fs.fsyncSync(file.fd); return true;
    } finally { fs.closeSync(file); }
  }
  async chooseImages(available: number): Promise<ExchangeImage[]> {
    if (available <= 0) throw new Error('每则备注最多 8 张图片。');
    const options = new photoAccessHelper.PhotoSelectOptions(); options.MIMEType = photoAccessHelper.PhotoViewMIMETypes.IMAGE_TYPE; options.maxSelectNumber = available;
    const selected = await new photoAccessHelper.PhotoViewPicker().select(options); const values: ExchangeImage[] = [];
    for (const uri of selected.photoUris) {
      const file = fs.openSync(uri, fs.OpenMode.READ_ONLY);
      try {
        if (fs.statSync(file.fd).size > 20000000) throw new Error('单张原图不能超过 20 MB。');
        const source = image.createImageSource(file.fd); let pixel: image.PixelMap | null = null; const packer = image.createImagePacker();
        try {
          const info = await source.getImageInfo(); const scale = Math.min(1, 2048 / Math.max(info.size.width, info.size.height));
          pixel = await source.createPixelMap({ desiredSize: { width: Math.max(1, Math.round(info.size.width * scale)), height: Math.max(1, Math.round(info.size.height * scale)) } });
          const bytes = new Uint8Array(await packer.packToData(pixel, { format: 'image/png', quality: 100 })); if (bytes.length > 10000000) throw new Error('处理后的图片超过 10 MB。');
          const value = new ExchangeImage(); value.Id = await this.crypto.hashBytes(bytes); value.FileName = '备注图片.png'; value.Data = this.crypto.encodeBase64(bytes); values.push(value);
        } finally { if (pixel) await pixel.release(); await packer.release(); await source.release(); }
      } finally { fs.closeSync(file); }
    }
    return values;
  }
}
