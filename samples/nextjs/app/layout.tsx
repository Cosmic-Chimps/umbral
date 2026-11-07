import type { Metadata } from "next";
import type { ReactNode } from "react";

export const metadata: Metadata = { title: "Umbral sample: Next.js" };

export default function Layout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
