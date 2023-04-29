export default class ErrorManager {

    logAjaxError(XMLHttpRequest, textStatus, errorThrown) {

        let message = '';

        if (XMLHttpRequest.responseText && XMLHttpRequest.responseText.length > 0) {
            message = XMLHttpRequest.responseText;
        } else {
            message = errorThrown;
        }

        let modal = `<div class="modal fade" id="modal-error-message" tabindex="-1" aria-labelledby="modal-label-error-message" aria-hidden="true" data-target="#staticBackdrop">
                        <div class="modal-dialog modal-dialog-centered">
                            <div class="modal-content">
                                <div class="modal-header">
                                    <h5 class="modal-title" id="modal-label-error-message">OOOPS something wrong happened!!!</h5>
                                    <button type="button" class="btn-close" data-mdb-dismiss="modal" aria-label="Close"></button>
                                </div>
                                <div class="modal-body">
                                    <div class="col-md-12">
                                        <p class="text-danger" id="p-error-message">
                                                ${message}
                                        </p>
                                    </div>
                                </div>

                                <div class="modal-footer">
                                    <button type="button" class="btn btn-sm btn-danger" data-mdb-dismiss="modal" id="button-translate-message">Close</button>
                                </div>
                            </div>
                        </div>
                    </div>`;

        $(modal).modal('toggle');

    }
}


