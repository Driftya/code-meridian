# Authority Matrix

| Capability                            | Observe | Recommend |        Prepare |        Publish |        Experiment | Shipped v1             |
| ------------------------------------- | ------: | --------: | -------------: | -------------: | ----------------: | ---------------------- |
| Read projections and audit            |     yes |       yes |            yes |            yes |               yes | yes                    |
| Run registered read-only sensors      |     yes |       yes |            yes |            yes |               yes | yes                    |
| Admit a human prompt as evidence      |     yes |       yes |            yes |            yes |               yes | yes                    |
| Read allowlisted RSS/Atom feeds       |     yes |       yes |            yes |            yes |               yes | disabled by default    |
| Read CodeMeridian graph diagnostics   |     yes |       yes |            yes |            yes |               yes | disabled by default    |
| Invoke a read-only reasoning provider |      no |       yes |            yes |            yes |               yes | yes                    |
| Run attention/affect/simulation cycle |      no |       yes |            yes |            yes |               yes | yes                    |
| Accept a human-authored goal          |      no |       yes |            yes |            yes |               yes | yes                    |
| Draft a proposed change               |      no |       yes |            yes |            yes |               yes | projection only        |
| Approve a candidate                   |      no |        no |     human only |     human only |        human only | human API command      |
| Write a repository workspace          |      no |        no | human approval | human approval | isolated approval | no                     |
| Publish or open a pull request        |      no |        no |             no | human approval |                no | no                     |
| Deploy or roll back production        |      no |        no |             no | human approval | isolated approval | no                     |
| Change governance or autonomy ceiling |      no |        no |             no |             no |                no | human API command only |
| Train or promote model parameters     |      no |        no |             no |             no | separate approval | no                     |
| Delete or rewrite journal history     |      no |        no |             no |             no |                no | no                     |

The configured level is a ceiling, not an entitlement. Every state change still requires valid
evidence, provenance, idempotency, and any capability-specific human authority.
