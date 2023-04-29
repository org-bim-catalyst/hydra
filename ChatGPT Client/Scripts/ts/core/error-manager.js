"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var ErrorManager = /** @class */ (function () {
    function ErrorManager() {
    }
    ErrorManager.prototype.logAjaxError = function (XMLHttpRequest, textStatus, errorThrown) {
        var message = '';
        if (XMLHttpRequest.responseText && XMLHttpRequest.responseText.length > 0) {
            message = XMLHttpRequest.responseText;
        }
        else {
            message = errorThrown;
        }
        var modal = "<div class=\"modal fade\" id=\"modal-error-message\" tabindex=\"-1\" aria-labelledby=\"modal-label-error-message\" aria-hidden=\"true\" data-target=\"#staticBackdrop\">\n                        <div class=\"modal-dialog modal-dialog-centered\">\n                            <div class=\"modal-content\">\n                                <div class=\"modal-header\">\n                                    <h5 class=\"modal-title\" id=\"modal-label-error-message\">OOOPS something wrong happened!!!</h5>\n                                    <button type=\"button\" class=\"btn-close\" data-mdb-dismiss=\"modal\" aria-label=\"Close\"></button>\n                                </div>\n                                <div class=\"modal-body\">\n                                    <div class=\"col-md-6 mx-auto\">\n                                        <p class=\"text-danger\" id=\"p-error-message\">\n                                                ".concat(message, "\n                                        </p>\n                                    </div>\n                                </div>\n\n                                <div class=\"modal-footer\">\n                                    <button type=\"button\" class=\"btn btn-sm btn-danger\" data-mdb-dismiss=\"modal\" id=\"button-translate-message\">Close</button>\n                                </div>\n                            </div>\n                        </div>\n                    </div>");
        $(modal).modal('toggle');
    };
    ErrorManager.prototype.isJsonObject = function (strData) {
        try {
            JSON.parse(strData);
        }
        catch (e) {
            return false;
        }
        return true;
    };
    return ErrorManager;
}());
exports.default = ErrorManager;
//# sourceMappingURL=error-manager.js.map