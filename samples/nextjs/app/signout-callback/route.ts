import { NextResponse } from "next/server";

export function GET() {
  return NextResponse.redirect(new URL("/", process.env.AUTH_URL));
}
