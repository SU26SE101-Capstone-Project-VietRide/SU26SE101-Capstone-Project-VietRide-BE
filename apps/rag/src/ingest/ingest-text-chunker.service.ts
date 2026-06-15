import { BadRequestException } from '@nestjs/common';
import {
  RAG_INGEST_CHUNK_OVERLAP_TOKENS,
  RAG_INGEST_CHUNK_TARGET_TOKENS,
  RAG_INGEST_MARKDOWN_HEADING_PATTERN,
} from './ingest.constants';
import type { RagTextChunk } from './ingest.types';

interface RagTextSection {
  sectionHeader: string | null;
  content: string;
}

export function chunkRagText(content: string): RagTextChunk[] {
  const normalized = normalize(content);
  if (!normalized) {
    throw new BadRequestException({
      errorCode: 'RAG_DOCUMENT_EMPTY_CONTENT',
      detail: 'Document content is empty',
    });
  }

  const sections = splitSections(normalized);
  const chunks: RagTextChunk[] = [];

  for (const section of sections) {
    const tokens = tokenize(section.content);
    if (tokens.length === 0) continue;
    chunks.push(...chunkTokens(section.sectionHeader, tokens, chunks.length));
  }

  if (chunks.length === 0) {
    throw new BadRequestException({
      errorCode: 'RAG_DOCUMENT_EMPTY_CONTENT',
      detail: 'Document content has no indexable text',
    });
  }

  return chunks;
}

function normalize(content: string): string {
  return content
    .replace(/^\uFEFF/, '')
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .replace(/[ \t]+$/gm, '')
    .trim();
}

function splitSections(content: string): RagTextSection[] {
  const lines = content.split('\n');
  const sections: RagTextSection[] = [];
  let currentHeader: string | null = null;
  let currentLines: string[] = [];

  for (const line of lines) {
    const heading = RAG_INGEST_MARKDOWN_HEADING_PATTERN.exec(line);
    const headingText = heading?.[2]?.trim();
    if (headingText) {
      pushSection(sections, currentHeader, currentLines);
      currentHeader = headingText;
      currentLines = [];
      continue;
    }
    currentLines.push(line);
  }

  pushSection(sections, currentHeader, currentLines);
  return sections.length > 0 ? sections : [{ sectionHeader: null, content }];
}

function pushSection(
  sections: RagTextSection[],
  sectionHeader: string | null,
  lines: string[],
): void {
  const content = lines.join('\n').trim();
  if (!content) return;
  sections.push({ sectionHeader, content });
}

function tokenize(content: string): string[] {
  // MVP trade-off: this is whitespace word count, not the provider's real tokenizer.
  return content.split(/\s+/).filter((token) => token.length > 0);
}

function chunkTokens(
  sectionHeader: string | null,
  tokens: string[],
  startIndex: number,
): RagTextChunk[] {
  const chunks: RagTextChunk[] = [];
  let cursor = 0;

  while (cursor < tokens.length) {
    const end = Math.min(cursor + RAG_INGEST_CHUNK_TARGET_TOKENS, tokens.length);
    const currentTokens = tokens.slice(cursor, end);
    chunks.push({
      chunkIndex: startIndex + chunks.length,
      sectionHeader,
      content: currentTokens.join(' '),
      tokenCount: currentTokens.length,
    });

    if (end >= tokens.length) break;
    cursor = Math.max(end - RAG_INGEST_CHUNK_OVERLAP_TOKENS, cursor + 1);
  }

  return chunks;
}
