"use client";

import { motion } from "motion/react";

/**
 * Re-mounts on every navigation (that's what a Next.js template is for), so each page arrives
 * the same way: rising out of a slight blur. Quick enough that it never feels like waiting.
 */
export default function PageTransition({ children }: { children: React.ReactNode }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 12, filter: "blur(8px)" }}
      animate={{ opacity: 1, y: 0, filter: "blur(0px)" }}
      transition={{ type: "spring", stiffness: 300, damping: 30 }}
      className="flex flex-1 flex-col"
    >
      {children}
    </motion.div>
  );
}
