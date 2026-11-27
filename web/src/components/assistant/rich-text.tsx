import { Fragment, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * The small slice of markdown that answers and passages use (paragraphs, bullet and numbered
 * lists, tables, headings, bold, code), turned into React elements. Nothing is ever injected as
 * HTML, so a model or a job note can't put markup on the page.
 *
 * In answers, [n] becomes a citation button through renderCitation.
 */
export function RichText({ text, renderCitation, className }: { text: string; renderCitation?: (n: number) => ReactNode; className?: string }) {
  const blocks = text.replace(/\r\n/g, "\n").split(/\n{2,}/).filter((b) => b.trim());
  return (
    <div className={cn("space-y-3 leading-relaxed", className)}>
      {blocks.map((block, i) => (
        <Block key={i} block={block.trim()} renderCitation={renderCitation} />
      ))}
    </div>
  );
}

function Block({ block, renderCitation }: { block: string; renderCitation?: (n: number) => ReactNode }) {
  const lines = block.split("\n");
  const inline = (s: string) => <Inline text={s} renderCitation={renderCitation} />;

  if (lines.every((l) => l.trim().startsWith("|"))) {
    const rows = lines.filter((l) => !/^\s*\|[\s|:-]+\|\s*$/.test(l)).map((l) => l.trim().replace(/^\||\|$/g, "").split("|").map((c) => c.trim()));
    const [head, ...body] = rows;
    return (
      <div className="overflow-x-auto rounded-xl border border-foreground/10">
        <table className="w-full text-left text-sm">
          <thead className="bg-foreground/[0.04]">
            <tr>{head.map((cell, i) => <th key={i} className="px-3 py-2 font-semibold">{inline(cell)}</th>)}</tr>
          </thead>
          <tbody>
            {body.map((row, r) => (
              <tr key={r} className="border-t border-foreground/[0.07]">
                {row.map((cell, c) => <td key={c} className="px-3 py-2 align-top">{inline(cell)}</td>)}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (lines.every((l) => /^\s*([-*]|\d+[.)])\s/.test(l))) {
    const ordered = /^\s*\d/.test(lines[0]);
    const List = ordered ? "ol" : "ul";
    return (
      <List className={cn("space-y-1.5 pl-5", ordered ? "list-decimal" : "list-disc marker:text-muted-foreground")}>
        {lines.map((l, i) => <li key={i}>{inline(l.replace(/^\s*([-*]|\d+[.)])\s+/, ""))}</li>)}
      </List>
    );
  }

  const heading = block.match(/^#{1,6}\s+(.*)$/);
  if (heading && lines.length === 1) {
    return <p className="font-semibold">{inline(heading[1])}</p>;
  }

  // A paragraph, keeping single line breaks (a list without a blank line before it, an address).
  return (
    <p>
      {lines.map((l, i) => (
        <Fragment key={i}>
          {i > 0 && <br />}
          {inline(l)}
        </Fragment>
      ))}
    </p>
  );
}

const TOKENS = /(\*\*[^*]+\*\*|`[^`]+`|\[\d+\])/g;

function Inline({ text, renderCitation }: { text: string; renderCitation?: (n: number) => ReactNode }) {
  return (
    <>
      {text.split(TOKENS).map((part, i) => {
        if (/^\*\*[^*]+\*\*$/.test(part)) return <strong key={i} className="font-semibold">{part.slice(2, -2)}</strong>;
        if (/^`[^`]+`$/.test(part)) return <code key={i} className="rounded bg-foreground/[0.06] px-1 py-0.5 text-[0.9em]">{part.slice(1, -1)}</code>;
        const citation = part.match(/^\[(\d+)\]$/);
        if (citation && renderCitation) return <Fragment key={i}>{renderCitation(Number(citation[1]))}</Fragment>;
        return <Fragment key={i}>{part}</Fragment>;
      })}
    </>
  );
}
