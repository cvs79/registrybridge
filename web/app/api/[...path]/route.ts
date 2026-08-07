import type { NextRequest } from "next/server";

const apiUrl = process.env.API_URL ?? "http://localhost:5000";

async function forward(request: NextRequest) {
  const target = new URL(`${request.nextUrl.pathname}${request.nextUrl.search}`, apiUrl);
  const headers = new Headers(request.headers);
  headers.delete("host");
  headers.delete("content-length");

  const response = await fetch(target, {
    body: request.method === "GET" || request.method === "HEAD" ? undefined : await request.arrayBuffer(),
    headers,
    method: request.method,
    redirect: "manual",
  });

  return new Response(response.body, {
    headers: response.headers,
    status: response.status,
    statusText: response.statusText,
  });
}

export const GET = forward;
export const PUT = forward;
export const POST = forward;
export const PATCH = forward;
export const DELETE = forward;
