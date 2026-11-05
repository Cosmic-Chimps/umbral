### Fixed

- The README's **Integrate an app** said an app gets no `email` when the provider had not
  verified it. Umbral has always sent such an address with `email_verified: false`; the README
  now says so, and that the email is the first one Umbral saw. It also said that two OpenID
  Connect providers never bring people together: a verified email links them as it does for
  every other connector. The section now lists the endpoints, what signing out does, and what an
  app sees when someone is disabled or deleted.
