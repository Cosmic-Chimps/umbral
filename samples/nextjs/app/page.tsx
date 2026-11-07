import { auth } from "@/auth";
import { signOutEverywhere } from "./actions";

export default async function Home() {
  const session = await auth();
  return (
    <>
      <h1>Umbral sample: Next.js</h1>
      <p>
        <a href="/public">Public</a> <a href="/private">Private</a>
      </p>
      <div>
        {session ? (
          <form action={signOutEverywhere}>
            <button type="submit">Sign out</button>
          </form>
        ) : (
          <a href="/signin">Sign in</a>
        )}
      </div>
    </>
  );
}
