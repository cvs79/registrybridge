import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "RegistryBridge Control Plane",
  description: "Operate a deployment-specific registry artifact catalog.",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className="h-full">
      <body className="h-full font-sans antialiased">{children}</body>
    </html>
  );
}
