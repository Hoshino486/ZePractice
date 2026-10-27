#include "named_map_request.h"
#include <iostream>
#include <stdexcept>
using namespace NamedWorkshop;
int main()
{
    int passed = 0;
    auto check = [&](bool ok, const char *name) { if (!ok) throw std::runtime_error(name); ++passed; std::cout << "PASS " << name << '\n'; };
    for (auto id : {"1", "3799577772", "18446744073709551615"}) check(ValidId(id), "valid ID");
    for (auto id : {"", "0", "-1", "1;quit", "18446744073709551616", " 123", "123\n"}) check(!ValidId(id), "invalid ID");
    for (auto map : {"ze_overkill_p", "ze_test-v1.2", "ze_secondary"}) check(ValidMap(map), "valid exact map name");
    for (auto map : {"", "../escape", "a/b", "a\\b", "ze_test;quit", "test\nquit", "\"test\"", "-option", ".hidden"}) check(!ValidMap(map), "unsafe map rejected");
    check(!ValidMap(std::string(182, 'a')), "overlong map rejected");
    const std::string a(32, 'a'), b(32, 'b'), c(32, 'c');
    check(ValidToken(a), "token accepted"); check(!ValidToken("a"), "short token rejected"); check(!ValidToken(std::string(32, 'g')), "nonhex token rejected");
    Requests requests;
    check(requests.Begin("123", "ze_secondary", a), "new request accepted");
    check(requests.Find("123")->map == "ze_secondary", "nondefault map retained");
    check(!requests.Begin("456", "ze_other", b), "concurrent request rejected");
    check(!requests.Cancel(b), "stale token cannot cancel request");
    check(!requests.Forget("123"), "pending selection cannot be cleared by unrelated default load");
    check(!requests.FailDownload("456"), "other addon failure ignored");
    check(requests.FailDownload("123"), "matching failed download transitions state");
    check(requests.Find("123")->phase == Phase::Failed, "late failed load remains rejectable");
    check(requests.Begin("456", "ze_other", b), "new request after failure accepted");
    check(requests.Find("123")->phase == Phase::Failed, "old failure retained across new request");
    check(requests.Cancel(b), "timeout cancels current request");
    check(requests.Find("456")->phase == Phase::Cancelled, "late cancelled load remains rejectable");
    check(requests.Begin("123", "ze_exact", c), "intentional retry replaces old failure");
    check(requests.Find("123")->map == "ze_exact" && requests.Find("123")->token == c, "retry uses new exact selection and token");
    requests.Find("123")->phase = Phase::Submitted;
    check(!requests.Cancel(c), "map-end cleanup cannot cancel committed map");
    check(!requests.FailDownload("123"), "late download error cannot invalidate committed map");
    check(requests.Forget("456") && !requests.Find("456"), "explicit default-map retry clears cancelled selection");
    check(!requests.Begin("789", "../escape", a) && requests.Current()->id == "123", "invalid request preserves current state");
    std::cout << "PASS " << passed << " native named-map policy checks\n";
}
