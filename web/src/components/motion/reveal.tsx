"use client";

import { motion, type HTMLMotionProps, type Variants } from "motion/react";

/**
 * Staggered entrances: a container whose children arrive one after another, rising out of a
 * slight blur. Used where content appears (a page loading, a list filling), never as decoration
 * on content that's already there.
 */

const container: Variants = {
  hidden: {},
  shown: { transition: { staggerChildren: 0.06, delayChildren: 0.04 } },
};

export const revealItem: Variants = {
  hidden: { opacity: 0, y: 14, filter: "blur(6px)" },
  shown: { opacity: 1, y: 0, filter: "blur(0px)", transition: { type: "spring", stiffness: 260, damping: 26 } },
};

/** Animates its RevealItem children in, either on mount or when scrolled into view. */
export function Reveal({ inView = false, ...props }: HTMLMotionProps<"div"> & { inView?: boolean }) {
  return (
    <motion.div
      variants={container}
      initial="hidden"
      {...(inView ? { whileInView: "shown", viewport: { once: true, margin: "-80px" } } : { animate: "shown" })}
      {...props}
    />
  );
}

export function RevealItem(props: HTMLMotionProps<"div">) {
  return <motion.div variants={revealItem} {...props} />;
}
