"use client";

import { motion } from "motion/react";

/**
 * The headline arrives a word at a time, each rising out of a blur. Lines are set on purpose (a
 * headline's breaks are part of its meaning), and on narrow screens each line wraps on its own.
 * It reads as one sentence to a screen reader: the words are split only visually.
 */
export function Headline({ lines, className }: { lines: string[]; className?: string }) {
  let order = 0;
  return (
    <h1 className={className} aria-label={lines.join(" ")}>
      {lines.map((line) => (
        <span key={line} aria-hidden className="block">
          {line.split(" ").map((word, index, words) => {
            const delay = 0.08 + order++ * 0.07;
            return (
              <motion.span
                key={`${word}-${index}`}
                initial={{ opacity: 0, y: "0.45em", filter: "blur(12px)" }}
                animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
                transition={{ type: "spring", stiffness: 200, damping: 22, delay }}
                className="inline-block whitespace-pre"
              >
                {word}
                {index < words.length - 1 ? " " : ""}
              </motion.span>
            );
          })}
        </span>
      ))}
    </h1>
  );
}
