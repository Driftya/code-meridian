export class SqlSourcePositionMap {
  private readonly bytes: Buffer;
  private readonly lineStarts: number[] = [0];

  constructor(text: string) {
    this.bytes = Buffer.from(text, 'utf8');
    for (let i = 0; i < this.bytes.length; i++) if (this.bytes[i] === 10) this.lineStarts.push(i + 1);
  }

  locate(byteOffset: number): { line: number; column: number } {
    const offset = Math.max(0, Math.min(byteOffset, this.bytes.length));
    let low = 0, high = this.lineStarts.length - 1;
    while (low < high) {
      const mid = Math.ceil((low + high) / 2);
      if (this.lineStarts[mid] <= offset) low = mid; else high = mid - 1;
    }
    return { line: low + 1, column: this.bytes.subarray(this.lineStarts[low], offset).toString('utf8').length + 1 };
  }
}

export function sourceLocation(text: string, byteOffset: number): { line: number; column: number } {
  return new SqlSourcePositionMap(text).locate(byteOffset);
}
