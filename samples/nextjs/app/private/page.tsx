import { redirect } from "next/navigation";
import { auth } from "@/auth";

const CLAIMS = ["sub", "name", "idp", "email", "email_verified"];

export default async function Private() {
  const session = await auth();
  // A page cannot set the cookies a sign-in needs, a route handler can: /signin
  // starts it and brings the person back here.
  if (!session) redirect("/signin?to=/private");
  const claims = session.claims ?? {};
  return (
    <>
      <h1>Private</h1>
      <table>
        <tbody>
          {CLAIMS.filter((name) => claims[name] != null).map((name) => {
            const value = claims[name];
            return (
              <tr key={name}>
                <th>{name}</th>
                <td>{typeof value === "string" ? value : JSON.stringify(value)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
      <p>
        <a href="/">Back</a>
      </p>
    </>
  );
}
